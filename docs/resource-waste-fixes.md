# 资源浪费 & Token 消耗修复清单

文档日期：2026-05-17  
分析范围：`AiChatApp/Services/`、`Endpoints/`、`Hubs/`

---

## 问题总览

| # | 问题 | 文件 | 严重度 | 类型 |
|---|------|------|--------|------|
| 1 | `MemoryConsolidationService` 计数器因 Scoped 注册永远不会触发采样 | `MemoryConsolidationService.cs:20` | P0 | Bug |
| 2 | `BuildHistoryBlockAsync` 中 `limit` 参数被 `Math.Max(limit,10)` 静默覆盖 | `AiService.cs:1074` | P1 | 逻辑错误 |
| 3 | `MEMORY INSTRUCTION` 无条件注入所有 System Prompt，迫使 AI 每次输出 `MEMORY:` 行 | `AiService.cs:895,1057` | P1 | Token 浪费 |
| 4 | `ProactiveBrainService.ProcessWelcomeInsightAsync` 每次 SignalR 连接都触发，无节流 | `ProactiveBrainService.cs:44` | P1 | Token 浪费 |
| 5 | `ProactiveBrainService.ProcessTodoChange` 每次创建 Todo 顺序触发 **两次** AI 调用 | `ProactiveBrainService.cs:160-161` | P1 | Token 浪费 |
| 6 | `MemoryGraphService` 注册为 Scoped，每请求重建图，1 分钟缓存从未生效 | `ServiceExtensions.cs:23` | P2 | 性能 |
| 7 | `SkillLearningService.LearnFromInteractionAsync` 是空实现，仍在每次协作后被调用 | `AiService.cs:431` | P2 | 死代码 |
| 8 | DAG 并行子任务未传递 `session`，每个子任务各自触发一次 DB Join 查询 | `AiService.cs:1464-1488` | P2 | DB 查询 |

---

## 详细说明与修复方案

### 问题 1 — MemoryConsolidationService 采样计数器失效

**根因**：`MemoryConsolidationService` 注册为 `Scoped`，每次 HTTP 请求创建新实例，`_consolidationCounter` 始终从 0 开始。每次请求只调用一次 `TryConsolidateAsync`，计数器自增到 1，`1 % 10 != 0` 永远提前返回，记忆提取**从未执行**。

**修复**：将 `_consolidationCounter` 改为 `static`，使其跨请求累积。

```csharp
// Before
private int _consolidationCounter;

// After
private static int _consolidationCounter;
```

---

### 问题 2 — BuildHistoryBlockAsync limit 参数被覆盖

**根因**：调用方传入 `limit: 5`，但代码用 `Math.Max(limit, 10)` 强制最小值为 10，参数实际无效，且原有注释说"slightly increase"但实际是翻倍。每条消息截断 500 字符，10 条 = 最多 5000 字符历史注入。

**修复**：直接使用 `limit` 参数，删除 `Math.Max` 包装。

```csharp
// Before
.Take(Math.Max(limit, 10))

// After
.Take(limit)
```

---

### 问题 3 — MEMORY INSTRUCTION 无条件注入

**根因**：`BuildSystemPromptAsync`（普通聊天）和 `RunAgentStepAsync`（协作模式）都无条件在 System Prompt 末尾注入 `MEMORY INSTRUCTION`。普通聊天没有 `ParseAndSaveMemoryAsync` 调用，这段指令白白消耗 completion token（AI 会输出 `MEMORY: key=value` 行）。

**修复**：
- `BuildSystemPromptAsync`：仅当 `chatSessionId.HasValue` 时注入
- `RunAgentStepAsync`：仅当 `chatSessionId.HasValue` 时注入（已有 `attemptNumber == 1` 条件，加 `&&` 即可）

```csharp
// BuildSystemPromptAsync - Before
sb.Append(GetSystemPromptTemplate("MemoryInstruction", "..."));

// After
if (chatSessionId.HasValue)
    sb.Append(GetSystemPromptTemplate("MemoryInstruction", "..."));
```

```csharp
// RunAgentStepAsync - Before
if (attemptNumber == 1)
    sb.AppendLine(GetSystemPromptTemplate("MemoryInstruction", "..."));

// After
if (attemptNumber == 1 && chatSessionId.HasValue)
    sb.AppendLine(GetSystemPromptTemplate("MemoryInstruction", "..."));
```

---

### 问题 4 — ProcessWelcomeInsightAsync 无节流

**根因**：`ProactiveAgentHub.OnConnectedAsync` 注释掉了对 `ProcessWelcomeInsightAsync` 的调用（已禁用）。但若将来重新启用，无任何防重入保护：用户刷新页面 = 2 次额外 AI 调用。

**修复**：在 `ProactiveBrainService` 中添加 per-user 冷却字典（30 分钟），防止在冷却期内重复触发。

```csharp
private static readonly ConcurrentDictionary<int, DateTime> _lastWelcomeTime = new();
private static readonly TimeSpan WelcomeCooldown = TimeSpan.FromMinutes(30);

public async Task ProcessWelcomeInsightAsync(int userId)
{
    var now = DateTime.UtcNow;
    if (_lastWelcomeTime.TryGetValue(userId, out var last) && now - last < WelcomeCooldown)
        return;
    _lastWelcomeTime[userId] = now;
    // ... rest of logic
}
```

---

### 问题 5 — ProcessTodoChange 触发两次 AI 调用

**根因**：创建 Todo 时顺序调用 `Summarizer`（AI 调用 #1）+ `HyperionBrain`（AI 调用 #2），两次调用都针对同一个任务内容。

**修复**：合并为单次 AI 调用，在同一个 prompt 中完成摘要和建议。

```csharp
// Before: 两次调用
var analysis = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.Summarizer, $"任务: {item.Title}...", item.UserId);
var advice   = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.HyperionBrain, $"用户创建了新任务...{analysis}", item.UserId);

// After: 单次调用
var advice = await aiService.ExecuteProactiveAgentAsync(
    ProactiveAgentProfiles.HyperionBrain,
    $"用户创建了新任务「{item.Title}」。描述：{item.Description}。请给出一条专业工程建议，不超过60字，使用 Markdown 加粗重点。",
    item.UserId);
```

---

### 问题 6 — MemoryGraphService Scoped 注册导致缓存无效

**根因**：`MemoryGraphService` 有 1 分钟的 `_lastBuildTime` 缓存，但注册为 `Scoped`，每次请求创建新实例，`_lastBuildTime` 始终是 `DateTime.MinValue`，缓存从未命中。

**修复**：改为 `Singleton` 注册（`_adjList` 已是 `ConcurrentDictionary`，天然线程安全）。

```csharp
// ServiceExtensions.cs - Before
services.AddScoped<MemoryGraphService>();

// After
services.AddSingleton<MemoryGraphService>();
```

---

### 问题 7 — LearnFromInteractionAsync 死代码调用

**根因**：`SkillLearningService.LearnFromInteractionAsync` 是占位实现，直接 `return Task.CompletedTask`，但 `CooperateAsync` 仍在每次协作后 fire-and-forget 调用它，产生不必要的 Task 调度。

**修复**：删除 `CooperateAsync` 中的调用。

```csharp
// Before
await _skillLearning.LearnFromInteractionAsync(task, finalResult, steps, userId);

// After: 删除此行
```

---

### 问题 8 — DAG 并行子任务各自重查 Session

**根因**：`RunSubtaskLayerAsync` 没有 `ChatSession` 参数，调用 `RunAgentStepAsync` 时不传 `session`，导致每个并行子任务都执行 `_db.ChatSessions.Include(Project).Include(Agents).FirstOrDefaultAsync()`。

**修复**：为 `RunSubtaskLayerAsync` 添加 `ChatSession? session` 参数，并传递给每个 `RunAgentStepAsync` 调用；同时在 `ExecuteTaskGraphAsync` 和 `ReviseFailedSubtasksAsync` 中传入外部已加载的 session。

---

## 预期收益

| 修复 | Token 节省 | 性能提升 |
|------|-----------|---------|
| #1 记忆提取计数器修复 | 恢复预期的 1/10 触发率（实际上是由无效变为有效） | — |
| #2 历史条数修复（10→5） | 每次请求节省 ~2500 字符 prompt | — |
| #3 MEMORY INSTRUCTION 条件化 | 无 Session 时节省每次 ~50 token prompt + ~20 token completion | — |
| #4 欢迎洞察节流 | 30 分钟内避免重复的 2× AI 调用 | — |
| #5 Todo 2次→1次 AI 调用 | 每次 Todo 创建节省 ~50% AI 调用 | — |
| #6 MemoryGraph 改 Singleton | — | 避免每请求重建图，减少文件 I/O |
| #7 删除死代码调用 | — | 消除无意义 Task 分配 |
| #8 DAG session 传递 | — | N 个并行子任务减少 N-1 次 DB Join |
