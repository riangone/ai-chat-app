# AiChatApp - 智能终端聊天封装工具 (v2.0)

AiChatApp 是一个现代化的 Web 聊天界面，它将专业的 AI CLI 工具（如 `gemini` 和 `gh-copilot`）与丰富、响应式的用户体验相结合。它具有长期记忆、多智能体协作任务执行和高级会话管理功能。

## 🚀 核心特性

- **多供应商支持**：在 `Gemini 2.0` 和 `GitHub Copilot`（通过 CLI 封装）之间无缝切换。
- **协作模式**：一种多智能体工作流，由 **Orchestrator（编排者）** 规划，**Executor（执行者）** 实现，以及 **Reviewer（审核者）** 验证输出。
- **长期记忆**：AI 会记住用户定义的某些事实和偏好。在对话过程中，系统会根据关键词标签检索记忆。
- **技能系统**：可开关的“插件”或指令，用于增强 AI 处理特定任务的能力。
- **现代 UI/UX**：
  - 使用 **HTMX** 构建，实现流畅的类 SPA 交互，无需重型 JS 框架。
  - 使用 **Tailwind CSS** 和 **DaisyUI**（包含 30 多种主题）进行样式设计。
  - 响应式 **抽屉式布局 (Drawer layout)**，支持手机/平板端。
  - 实时 **Markdown 渲染**（使用 `marked.js`）。
  - 原生交互体验的消息操作：复制、转发（引用）以及保存到记忆。
- **会话管理**：完整的历史记录追踪、聊天重命名及即时创建新会话。
- **安全身份验证**：基于 Cookie 的身份验证，采用 **BCrypt** 密码哈希加密。

## 🛠 技术栈

- **后端**：ASP.NET Core 10 Minimal API。
- **数据库**：SQLite 与 Entity Framework Core。
- **AI 集成**：自定义 `AiService` 封装 CLI 进程 (`System.Diagnostics.Process`)。
- **前端**：HTMX, Tailwind CSS, DaisyUI, Marked.js。
- **安全**：BCrypt.Net-Next 处理哈希，Cookie 身份验证。

## 📂 项目结构

```text
AiChatApp/
├── Data/            # EF Core DbContext
├── Models/          # 数据模型 (User, Message, ChatSession, Memory, Skill)
├── Services/        # AiService (AI 逻辑与 CLI 交互)
├── wwwroot/         # 前端 (HTMX + Tailwind + DaisyUI)
│   ├── index.html   # 主聊天界面
│   ├── login.html   # 身份验证页面
│   └── register.html
├── Program.cs       # 应用程序入口点与 API 端点
└── AiChatApp.csproj # 依赖项与配置
```

## ⚙️ 环境准备

1. 安装 **.NET 10 SDK**。
2. **AI CLI 工具**：
   - 安装 `gemini` CLI（需添加到系统 PATH）。
   - 安装 `gh-copilot`（可选，用于支持 GitHub Copilot）。

## 🏃 快速上手

### 1. 克隆并构建
```bash
dotnet build
```

### 2. 配置数据库
应用程序使用 SQLite (`chat.db`)。数据库及其架构将在首次运行时自动创建。

### 3. 运行应用程序
```bash
dotnet run --project AiChatApp
```
应用将在 `http://localhost:5000` 运行。

## 🧠 高级概念

### 协作模式 (多智能体)
启用后，`AiService` 会启动一个三步流水线：
1. **Orchestrator**：分析请求并将其拆分为计划。
2. **Executor**：遵循计划生成代码或详细逻辑。
3. **Reviewer**：审核执行情况并提供最终润色后的响应。

### 长期记忆
用户可以将特定的信息片段以“记忆”形式保存，并附带标签。当用户消息包含与标签匹配的关键词时，`AiService` 会自动将该记忆作为“已知事实”注入到 AI 的系统提示词中。

### 技能系统
技能本质上是系统级的指令，可以手动开启或关闭。当启用某项技能且在提示词中提到其名称时，其特定的指令集将在系统提示词中获得高优先级。

## 📝 许可证
本项目仅供内部/教学使用。有关 AI 使用条款，请参阅相应 CLI 工具的许可证。
