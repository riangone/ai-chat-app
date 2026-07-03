# Loop Engineering 详细设计书

参考: https://addyosmani.com/blog/loop-engineering/
状态: Draft → 待 opencode 实现
关联现状代码: `Services/ProjectPulseService.cs`, `Services/Infrastructure/CliExecutorService.cs`,
`Services/SkillManagerService.cs`, `Services/AiService.cs`(`CooperateAsync`), `Services/Harness/EvalService.cs`

## 0. 目标与非目标

**目标**：把 `ProjectPulseService` 从"哨兵"（发现变更 → 通知）升级为"闭环"（发现 → 隔离 → 修复/生成 → 自审 → 落盘 → 记账），并补齐状态账本和 worktree 隔离两块地基。

**非目标**：
- 不做真正的 issue tracker / CI 集成（Connectors 只留可插拔接口，本期不接外部 SaaS）
- 不做无人值守自动合并到主分支。默认所有产出落在独立分支 + worktree，是否合并到 `main` 由人决定（`AutoMerge=false` 是默认且推荐值）
- 不改动现有 `CooperateAsync` 三段式管线的核心逻辑，只复用它

## 1. 六要素落地对照

| 要素 | 现状 | 本次改动 |
|---|---|---|
| Skills | 已有 | 不动 |
| Sub-agents | 已有（Orchestrator→Executor→Reviewer + EvalService 打分） | 复用，作为"修复执行者" |
| Automations | 半成品（单目录、只通知） | 重写为多项目轮询 + 落地执行 |
| 状态账本 | 缺 | 新增 `ProjectPulseLedger` 表 |
| Worktrees | 缺 | 新增 `WorktreeManagerService` |
| Connectors/MCP | 缺 | 只定义 `IPulseSource` 接口 + Git 实现，预留扩展位 |

## 2. 数据模型

### 2.1 新实体 `Models/ProjectPulseLedger.cs`

```csharp
namespace AiChatApp.Models;

public class ProjectPulseLedger
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    // "git_commit" 固定值（本期唯一来源），为将来 issue/ci_failure 预留
    public string SourceType { get; set; } = "git_commit";

    // 去重键：本期用 "{oldHash}..{newHash}"
    public string SourceKey { get; set; } = "";

    // pending -> running -> succeeded | failed | skipped
    public string Status { get; set; } = "pending";

    // 本次处理创建的 worktree 分支名，便于人工检视/清理
    public string? BranchName { get; set; }

    // Reviewer/EvalService 给出的结论摘要（成功时是变更说明，失败时是失败原因）
    public string? ResultSummary { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
```

去重规则：`(ProjectId, SourceType, SourceKey)` 唯一。处理前先查表，命中且状态为 `succeeded`/`skipped` 则跳过；命中且为 `failed` 允许重试（不新建行，更新原行）；命中且为 `running` 说明有并发/上次异常退出，超过 30 分钟视为僵死，允许重跑。

### 2.2 `AppDbContext.cs`
新增 `public DbSet<ProjectPulseLedger> ProjectPulseLedgers { get; set; }`

### 2.3 `ApplicationExtensions.InitializeDatabaseAsync`
按现有模式（`CREATE TABLE IF NOT EXISTS`，因为 `EnsureCreated()` 对已存在的库不会补表）追加：

```sql
CREATE TABLE IF NOT EXISTS ProjectPulseLedgers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectId INTEGER NOT NULL,
    SourceType TEXT NOT NULL DEFAULT 'git_commit',
    SourceKey TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'pending',
    BranchName TEXT,
    ResultSummary TEXT,
    CreatedAt TEXT NOT NULL,
    CompletedAt TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ProjectPulseLedgers_Dedup
    ON ProjectPulseLedgers(ProjectId, SourceType, SourceKey);
```

## 3. Worktree 隔离

### 3.1 新服务 `Services/WorktreeManagerService.cs`

职责：为一次 pulse 处理创建/清理独立 git worktree，避免与用户当前工作目录、其他并发 agent 互相踩踏。

```csharp
public class WorktreeManagerService
{
    // 约定路径: {Project.RootPath}/../.worktrees/{ProjectName}-{ledgerId}
    public Task<string> CreateAsync(string repoRootPath, string branchName, CancellationToken ct);
    // git worktree add -b {branchName} {path} {baseHash}

    public Task RemoveAsync(string worktreePath, string branchName, bool deleteBranch, CancellationToken ct);
    // git worktree remove --force {path}; 可选 git branch -D {branchName}
}
```

关键点：
- `worktree add` 的目标目录放在仓库外层的 `.worktrees/` 下（同级目录，不进 `.git` 追踪范围），避免污染 `RootPath` 本身
- 分支命名：`pulse/{yyyyMMdd-HHmmss}-{ledgerId}`
- 失败/成功都要 `RemoveAsync`（成功时保留分支、删除工作树；失败时两者都可删，取决于 `ProactiveSettings:KeepFailedBranches` 配置，默认 `true` 方便人工排查）
- 并发保护：`SemaphoreSlim` 按 `ProjectId` 分桶，同一项目同一时刻只允许一个 worktree 处理（多项目间可并行）

## 4. Pulse Source 抽象（为 Connectors 预留）

```csharp
// Services/Pulse/IPulseSource.cs
public interface IPulseSource
{
    string SourceType { get; } // "git_commit"
    Task<IReadOnlyList<PulseItem>> DiscoverAsync(Project project, CancellationToken ct);
}

public record PulseItem(string SourceKey, string Description, string? BaseRef);
```

本期只实现 `GitCommitPulseSource`（把现有 `ProjectPulseService` 里 `GetCurrentCommitHashAsync`/`GetDiffSummaryAsync` 逻辑搬过来）。未来加 `GithubIssuePulseSource` / `CiFailurePulseSource` 时只需新增实现并在 DI 里注册，不改主循环。

## 5. `ProjectPulseService` 重写

### 5.1 主循环变化
- 不再依赖单一 `FileWatcher:Path`；改为 `db.Projects` 逐项目遍历（`Project.RootPath` 必须是 git 仓库，非 git 目录 `git rev-parse` 失败则跳过并 `LogDebug`）
- 每个 Project 独立维护"上次已知 commit"——不再用内存变量，而是查 `ProjectPulseLedgers` 里该 Project 最新一条记录的 `SourceKey` 反推（或新增 `ProjectPulseCursor` 极简表存 `ProjectId -> LastKnownHash`，二选一，**采用后者**更直接，避免用 Ledger 反查耦合语义）：

```csharp
// Models/ProjectPulseCursor.cs
public class ProjectPulseCursor
{
    public int ProjectId { get; set; } // PK
    public string LastKnownHash { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```
（对应 `CREATE TABLE IF NOT EXISTS ProjectPulseCursors (ProjectId INTEGER PRIMARY KEY, LastKnownHash TEXT NOT NULL, UpdatedAt TEXT NOT NULL);`）

- 首次见到某 Project：写入当前 hash 作为 cursor，不触发分析（与现有首跑行为一致）
- 检测到新 commit → 生成 `SourceKey = "{old}..{new}"` → 查 Ledger 去重 → 未处理则调用 `PulseActionService.ExecuteAsync(project, sourceKey, old, new, ct)`

### 5.2 Runtime 配置（`appsettings.json` 新增 `ProactiveSettings` 子项）
```json
"ProactiveSettings": {
  "Enabled": true,
  "CheckIntervalMinutes": 10,
  "AutoFixEnabled": false,
  "KeepFailedBranches": true,
  "MaxConcurrentProjects": 2
}
```
`AutoFixEnabled=false` 是默认值：为 false 时只做现有行为（分析+通知，不建 worktree/不跑修复），保证这次改动默认不改变生产行为，需管理员显式开启才进入"真正闭环"模式。这是对文章里"验证责任不转移"警告的直接呼应——先让人观察通知阶段稳定运行一段时间，再开自动修复。

## 6. `PulseActionService`（新增，闭环核心）

```csharp
public class PulseActionService
{
    public async Task ExecuteAsync(Project project, string sourceKey, string oldHash, string newHash, CancellationToken ct)
    {
        // 1. 账本登记 status=running（若 AutoFixEnabled=false，登记 status=skipped 并直接走旧的通知逻辑，return）
        // 2. diffSummary = 复用现有 GetDiffSummaryAsync 逻辑
        // 3. worktreePath = await _worktreeManager.CreateAsync(project.RootPath, branchName, ct)
        // 4. 组装 prompt：把 diffSummary + project 的既有 Agents 配置交给 AiService.CooperateAsync
        //    workingDirectory 传 worktreePath（不是 project.RootPath！这是隔离的关键）
        //    onStepComplete 回调仍然经 SignalR 推送到 project-{id} 组，UI 体验与现状一致
        // 5. CooperateAsync 内部已有 Reviewer 阶段；额外用 EvalService 对最终结果打分，
        //    Accuracy/Safety 任一低于阈值（如 0.6）→ status=failed，KeepFailedBranches 决定是否留分支供人工看
        // 6. 通过 → git -C {worktreePath} add -A && commit -m "pulse: <summary>"
        //    不 push、不开 PR（本期不接 GitHub API）；在 ResultSummary 里记录分支名，
        //    UI/通知里提示"分支已就绪，请人工审查后合并"
        // 7. RemoveAsync worktree（保留分支）
        // 8. 更新 Ledger：status, BranchName, ResultSummary, CompletedAt
        // 9. 更新 ProjectPulseCursor.LastKnownHash = newHash
    }
}
```

这一步把"警告2：理解债"具象化为强制动作——每次自动修复都留下一个可 diff、可 `git log` 追溯的独立分支，而不是直接改主分支，人必须主动 `git merge` 才算数，天然形成 review checkpoint。

## 7. 可观测性（UI 最小改动）

- 新增只读端点 `GET /api/projects/{id}/pulse-ledger`（`ProjectApiController` 或新建 `PulseEndpoints.cs`），分页返回该项目最近 N 条 `ProjectPulseLedger`，供前端渲染一个简单时间线（复用现有 Notes/Todo 列表的 HTMX 片段风格）
- SignalR `ProactiveMessage` 事件里追加 `ledgerId` 字段，方便前端点击通知跳转到对应记录

## 8. 实施任务拆分（交给 opencode 的顺序）

1. `Models/ProjectPulseLedger.cs`、`Models/ProjectPulseCursor.cs` + `AppDbContext` 注册两个 DbSet
2. `ApplicationExtensions.InitializeDatabaseAsync` 追加两张表的 `CREATE TABLE IF NOT EXISTS`
3. `Services/WorktreeManagerService.cs`（含单元可测的 `CreateAsync`/`RemoveAsync`，用 `ProcessStartInfo` 跑 `git worktree`）
4. `Services/Pulse/IPulseSource.cs` + `Services/Pulse/GitCommitPulseSource.cs`（把 `ProjectPulseService` 里现有的 hash/diff 逻辑原样搬迁，不改行为）
5. `Services/PulseActionService.cs`（第 6 节核心逻辑，依赖 `AiService`、`EvalService`、`WorktreeManagerService`、`AppDbContext`）
6. 重写 `Services/ProjectPulseService.cs`：改为遍历 `db.Projects`，用 cursor 表替换 `_lastProcessedCommit` 字段，调用 `PulseActionService`
7. `appsettings.json` 追加 `ProactiveSettings` 默认块（`AutoFixEnabled: false`）
8. `Extensions/ApplicationExtensions`（DI 注册）里补充新服务的 `AddScoped`/`AddSingleton`
9. `Endpoints/PulseEndpoints.cs`（新建）+ `Program.cs` 里 `app.MapPulseEndpoints()`
10. 编译验证：`dotnet build AiChatApp`

每一步应保持可独立编译通过，第 6 步之前旧代码路径不受影响（向后兼容），便于分批 review opencode 的产出。

## 9. 风险对照文章三条警告

| 文章警告 | 本设计的对策 |
|---|---|
| 验证责任不会转移给循环 | `EvalService` 阈值门禁 + 强制走独立分支，人工合并前无生产影响 |
| 快是快，但欠"理解债" | 每条 `ProjectPulseLedger` 记录留痕（diff 摘要、分支名、评分结果），可追溯 |
| 循环可能是逃避思考的借口 | 默认 `AutoFixEnabled=false`，只有管理员显式开启才进入自动修复，且永不自动合并/push |
