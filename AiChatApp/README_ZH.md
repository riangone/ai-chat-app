# AiChatApp — 多代理协作 AI 聊天平台

基于 ASP.NET Core 10 构建的 Web 聊天平台，将 `gemini`、`claude`、`copilot`、`codex`、`opencode` 等 AI CLI 工具统一封装，支持**多代理并行协作**、长期记忆、技能注入和实时推送。

---

## 核心特性

### 多代理协作（DAG 并行执行）

协作模式下，系统自动完成以下流程：

```
用户输入
  ↓
Orchestrator → 输出结构化任务图（JSON，含 id / agent / deps）
  ↓ 拓扑排序
Layer 1: [t1, t2]  ← Task.WhenAll 并行执行
  ↓ 各自写入 Blackboard
Layer 2: [t3]      ← 读取 Blackboard["t1"] 作为上游上下文
  ↓
Reviewer → per-subtask 结构化反馈（verdict / score / issues）
  ↓ 若 verdict == "revision_needed"
    只重跑失败子任务 + 下游依赖，将具体 issue 注入 prompt
  ↓ 再过一遍 Reviewer
返回最终结果
```

**Blackboard 共享工作空间**：各代理向命名的产物槽写入输出，后续代理按依赖关系读取，避免全文盲目传递。

### AI 供应商

| CLI | 启动参数 | 说明 |
|-----|---------|------|
| `gemini` | `--yolo` | 默认供应商 |
| `claude` | `--dangerously-skip-permissions` | 长上下文代码 |
| `copilot` | `--yolo --silent` | GitHub Copilot |
| `codex` | `exec --dangerously-bypass-approvals-and-sandbox` | 沙箱执行 |
| `opencode` | — | OpenCode CLI |

每个代理可通过 `AgentProfile.PreferredProvider` 独立指定供应商。

### 长期记忆与自动整合

- **记忆注入**：`BuildSystemPromptAsync` 按关键词匹配 `LongTermMemory.Tags`，自动注入系统提示
- **自动整合**：每次对话后 fire-and-forget 调用 `MemoryConsolidationService`，AI 提取事实写入 `LongTermMemory`
- **技能注入**：`Skill.TriggerKeywords` 匹配当前提示，注入特定指令；`TriggerKeywords` 为空则全局生效

### 主动感知（哨兵系统）

- `FileWatcherService`：监控项目目录文件变更，实时通过 SignalR 推送到前端
- `ProjectPulseService`：每 10 分钟增量扫描 Git 提交，触发 AI 主动分析
- **AI 动态简报**：首页根据当前时间、待办进度和未读建议，通过 Gemini 动态生成个性化的温馨问候和今日摘要（支持 Markdown）。

### 技能文件系统（SKILL.md）

代理定义从以下目录自动加载：

```
AgentSkills/System/*/SKILL.md   ← 系统内置代理
test-skill/SKILL.md
.gemini/skills/*/SKILL.md
```

支持 YAML Front Matter：

```markdown
---
name: MyAgent
description: 代理描述
---
系统提示词正文
```

文件系统定义优先于数据库中的同名 `AgentProfile`。

### Harness 评估系统

- **Pipeline 定义**：`pipelines/*.json`，支持多阶段、重试、schema 验证
- **Prompt 模板**：`pipelines/prompts/*.md`
- **Schema 验证**：`pipelines/schemas/*.json`（含 `orchestrator_output.json`、`reviewer_output.json`）
- **自动评估**：每步完成后 AI 按 Accuracy / Safety / Format / Helpfulness 四项打分

---

## 快速上手

### 环境要求

- .NET 10 SDK
- 至少一个 AI CLI 在 PATH 中：`gemini`（默认）、`claude`、`copilot`、`codex`、`opencode`

### 启动

```bash
# 构建
dotnet build AiChatApp

# 运行（http://localhost:5000）
dotnet run --project AiChatApp
```

首次启动自动创建 SQLite 数据库（`chat.db`）和默认管理员账户。

### 默认管理员账户

| 用户名 | 密码 |
|--------|------|
| admin | admin123 |

**生产环境**请设置环境变量覆盖默认密码：

```bash
export ADMIN_INITIAL_PASSWORD=your_strong_password
dotnet run --project AiChatApp
```

### 重置数据库（schema 变更时）

```bash
rm AiChatApp/chat.db && dotnet run --project AiChatApp
```

---

## 项目结构

```
AiChatApp/
├── Data/
│   └── AppDbContext.cs          # EF Core DbContext
├── Endpoints/                   # Minimal API 端点
│   ├── AuthEndpoints.cs         # 登录 / 注册 / 管理员
│   ├── ChatEndpoints.cs         # 聊天 / SSE 流式 / 协作
│   ├── MemoryEndpoints.cs       # 长期记忆 CRUD
│   ├── SkillEndpoints.cs        # 技能 CRUD
│   ├── HarnessEndpoints.cs      # Pipeline 管理与评估
│   ├── CliEndpoints.cs          # CLI 会话浏览
│   ├── TodoEndpoints.cs
│   ├── NotesEndpoints.cs
│   ├── FileManagerEndpoints.cs
│   └── StatsEndpoints.cs
├── Extensions/
│   ├── ServiceExtensions.cs     # DI 注册
│   └── ApplicationExtensions.cs # DB 初始化 / schema 补丁
├── Hubs/
│   └── ProactiveAgentHub.cs     # SignalR WebSocket 实时推送
├── Models/                      # 数据模型
│   ├── Message.cs               # User / ChatSession / Message / AgentStep
│   ├── AgentProfile.cs
│   ├── LongTermMemory.cs
│   ├── Skill.cs
│   ├── Project.cs
│   ├── Note.cs / TodoItem.cs / InputHistory.cs
│   └── Harness/                 # PipelineConfig / Evaluation
├── Services/
│   ├── AiService.cs             # 核心 AI 调用 / DAG 执行 / Blackboard
│   ├── MemorySearchService.cs   # 记忆 / 技能检索
│   ├── MemoryConsolidationService.cs  # 自动记忆整合
│   ├── SessionMemoryService.cs
│   ├── SkillManagerService.cs / SkillLearningService.cs
│   ├── ProjectService.cs / ProjectApiController.cs
│   ├── ProactiveBrainService.cs
│   ├── FileWatcherService.cs    # 文件变更哨兵
│   ├── ProjectPulseService.cs   # Git 增量扫描哨兵
│   └── Harness/
│       ├── PipelineLoaderService.cs
│       ├── EvalService.cs
│       ├── SchemaValidationService.cs
│       └── ToolExecutorService.cs
├── AgentSkills/System/          # 内置代理 SKILL.md 目录
├── pipelines/                   # Pipeline JSON + prompts + schemas + policies
├── wwwroot/                     # 前端（HTMX + Tailwind CSS + DaisyUI）
│   ├── index.html               # 主聊天界面（PWA）
│   ├── login.html / register.html
│   └── todo/                    # Todo PWA
├── memory/                      # AI 长期记忆文件（Markdown）
├── Program.cs                   # 应用入口
└── appsettings.json             # AI 供应商 / 超时 / 路径配置
```

---

## 配置说明

`appsettings.json` 关键字段：

```json
{
  "AiSettings": {
    "DefaultProvider": "gemini",
    "FallbackProvider": "gemini",
    "TimeoutSeconds": 600
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chat.db"
  },
  "FileWatcher": {
    "Path": "/path/to/watch"
  }
}
```

---

## API 端点概览

### 聊天

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/chat` | 普通聊天 / 协作模式（HTML 片段返回） |
| GET | `/api/chat/stream` | 普通聊天 SSE 流 |
| GET | `/api/chat/cooperate/stream` | 协作多代理 SSE 流 |

SSE 事件类型（协作流）：`session` → `step-complete` → `final` → `done`

### 其他

| 路径前缀 | 说明 |
|---------|------|
| `/api/auth` | 登录 / 注册 / 个人资料 |
| `/api/memory` | 长期记忆 CRUD |
| `/api/skills` | 技能管理 |
| `/api/harness` | Pipeline 定义 / 运行 / 评估 |
| `/api/projects` | 项目与自定义代理 |
| `/api/todo` | Todo 列表 |
| `/api/notes` | 笔记 |
| `/api/stats` | 使用统计 |
| `/hub/proactive-agent` | SignalR WebSocket |

---

## 技术栈

| 层 | 技术 |
|----|------|
| 后端 | ASP.NET Core 10 Minimal API |
| 数据库 | SQLite + Entity Framework Core |
| 实时通信 | SignalR WebSocket |
| 前端 | HTMX + Tailwind CSS + DaisyUI + marked.js |
| AI 集成 | 子进程 CLI 调用（`System.Diagnostics.Process`） |
| 认证 | Cookie 认证 + BCrypt 密码哈希 |
| PWA | Web App Manifest + Service Worker |

### 最近改进

- **修复 opencode/copilot 调用**: 解决非流式调用时缺失提示词参数导致的失败问题。
- **增强 Token 统计**: 支持 `opencode` 和 `gemini` 的嵌套 Token 结构解析，消耗统计更精准。
- **UI/UX 优化**: 侧边栏“Workspace”更名为“TODO”并默认固定在标签栏，提升待办协作体验。
- **简报缓存**: 实现 AI 动态简报 30 分钟缓存，显著提升首页加载速度并节省额度。

---

## 许可证

本项目仅供内部 / 学习使用。AI CLI 工具的使用条款请参阅各自官方许可证。
