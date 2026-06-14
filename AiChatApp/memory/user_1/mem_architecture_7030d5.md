---
name: architecture,localization,files
description: ### 1. AiChatApp 核心架构与 UI 系统
- **系统定位与基础**：AiChatApp（也称为 ai-chat-pro）是一个基于 ASP.N...
type: user
userId: 1
tags: architecture,localization,files
relations: AiChatApp,NetYamlForge,Hyperion,GeminiProxyServer,ai-launcher,JPCS
relevanceScore: 90
accessCount: 72
createdAt: 2026-06-09T05:34:42.2897193Z
lastAccessedAt: 2026-06-14T11:08:41.0821164Z
boundAgentRole: 
---

### 1. AiChatApp 核心架构与 UI 系统
- **系统定位与基础**：AiChatApp（也称为 ai-chat-pro）是一个基于 ASP.NET Core 10 Minimal API 和 Gemini CLI 构建的 AI 个人助手框架。项目从早期的 WinForms 转向 ASP.NET WebForms / Minimal API，旨在通过 Chrome 浏览器扩展（Content Script）对老旧 WebForms 系统进行 AI 注入。此外，系统内置了 Todo、Notes、Finance、FileManager（文件管理器）及 Proactive Suggestions（主动建议）等功能模块。
- **前端与交互设计**：
  - 主界面采用 Swiper 幻灯片，导航索引顺序统一同步为：0: AI Chat, 1: History, 2: Workspaces, 3: Skills, 4: Memory, 5: CLI Sessions, 6: Notes, 7: Harness Engineering, 8: Pipeline Logs, 9: Settings, 10: File Manager (Files)。
  - 界面图层构成：Layer 0 (锁定屏幕)、Layer 1 (主屏幕)、Layer 2 (上下文面板) 和 Layer 3 (AI 聊天覆盖层)。
  - 实时对话前端通过 SSE (Server-Sent Events) 流式传输（通过 fetch 请求 `/api/chat/stream` 并使用 Reader 实时读取渲染）。历史消息加载通过 `/api/chat/load/{id}` 接口结合 HTMX，将后端渲染的 HTML 片段动态插入到 `#chat-panel` 中。前端还集成了 HTMX 以提供实时对话侧边栏。

### 2. Chrome 浏览器扩展与本地宿主架构
- **扩展组成**：Chrome 扩展基于 Manifest V3 (MV3) 规范，包含 `manifest.json`、用于处理 Native Messaging 通信与 AI Prompt 构造的后台脚本 `background.js`，以及采用 Shadow DOM 构建隔离 UI 边界的前台脚本 `content.js`。
- **本地宿主**：扩展通过 Native Messaging 协议与本地 Python 宿主进程 `native/antigravity_host.py` 进行双向数据通信。

### 3. AI 服务与多代理系统（Multi-agent）
- **AI 服务层（AiService.cs）重构**：
  - 原 `AiService.cs` 是一个约 700 行的“上帝类”（God Class），混合了 Prompt 构建、AI 执行、多代理协作及响应后处理等多重职责。建议将其重构为外观模式（Facade），拆分为 `AiPromptService`（上下文与 Prompt 构建）、`AiCollaborationService`（多代理协作逻辑）和 `AiResponseProcessor`（结果后处理）三个子服务。
  - 在系统架构上，使用 `IServiceProvider` 解决了 `AiService` 与 `MemorySearchService` 之间的循环依赖问题。
- **降级与执行机制**：AI 服务（如 `CliExecutorService.cs` 和 `AiService.cs`）具备自动降级机制，若主服务提供商（如 'opencode'）失败或返回空，将自动调用 'gemini' 提供商，但在失败情况下这会导致双重 CLI 执行。
- **混合 OCR 架构**：系统整合了“传统 OCR + AI 语义增强”架构，首先通过 Tesseract 提取文本，再由 Gemma 进行纠错与优化。
- **多代理协作与调度**：系统实现了多代理协作流程及相关测试，包括层次化进程（OrchestratorPlan）、黑板模式上下文传递（TaskBlackboard）以及 Review/Revision 机制。当前正在对 CrewAI（核心概念包括 Agent, Task, Crew, Flow）与 ai-chat-app 进行对比分析。
- **AI 管理数据底座**：AI 管理功能已完成第一阶段（数据基盘与 UI 层）和第二阶段（AI 执行引擎），定义了包括 `ai_decisions`、`ai_quotes` 在内的 5 个数据库表及对应实体文件。

### 4. 记忆系统架构（Hyperion）
- **双层记忆架构**：系统记忆分为基于 SQLite 的会话记忆（短期）和基于 Markdown 文件的长期记忆（代号 Hyperion，存储在 `/memory` 目录或项目目录下的 `.md` 文件中，用于持久化 AI 代理的认知状态）。
- **记忆服务组件**：核心服务包括 `MemorySearchService.cs`（搜索）、`MemoryConsolidationService.cs`（巩固）和协作协调器 `AiCollabService.cs`。
- **隔离与共享**：修改了 `MemoryFileService.cs`，实现了内存分层隔离与共享记忆模型。

### 5. GeminiProxyServer 与 ai-launcher 代理层
- **GeminiProxyServer**：这是一个独立的 ASP.NET Core 中转/代理服务器（位于 `/home/ubuntu/ws/ai-proxy-server`，该目录同时收纳了代理服务与 Chrome 扩展），通过 `/api/parse` 和 `/api/health` 接口调用 Gemini CLI，以避免在 AiChatApp 中直接实现结构化解析 API。
- **ai-launcher**：作为后台独立的 Node.js HTTP 服务（运行在端口 3002，PM2 进程名为 `ai-launcher-backend`），通过 `child_process.spawn` 直接调用多种 CLI 工具（gemini, claude, copilot, codex, opencode）。后端已改为支持多 CLI 调用，且取消了流式传输，改为返回 JSON 响应。

### 6. 邮件与主动建议系统
- **邮件服务（AiEmailChatExecutor.cs）**：负责处理核心逻辑，包括调用本地 Gemini CLI 生成邮件回复（`GenerateGeminiCliReplyAsync`）和发件人过滤。为防止诊断日志泄漏到回复邮件中，已重构为使用 JSON 输出模式（`-o json`）调用 Gemini CLI。此外，为兼容单例运行的 `SendEmailHook` 并防止启动崩溃，已将 `IEmailService` 的 DI 生命周期修改为 Singleton。
- **主动建议服务（Proactive Suggestions）**：其架构支持在广播前进行数据库持久化，通过 `AppDbContext` 管理 `ProactiveSuggestion` 模型，用于跟踪用户 ID（`UserId`）及已读状态（`IsRead`）。

### 7. NetYamlForge 框架与子项目独立化架构
- **子项目独立化与动态路由**：NetYamlForge 采用“子项目独立化”架构，核心原则是将特定功能的控制器、模型和视图完整迁移到 `projects/{project_name}/` 目录（例如 `NetYamlForge/projects/jpcs/`），实现逻辑独立。系统不采用 ASP.NET Core 标准的 Areas，而是通过 `ProjectMiddleware` 进行动态路由解析。所有内部重定向和链接需通过 Url.Content 配合基础路径 `/nyf`（PathBase）进行处理。
- **热重载与动态编译卸载机制**：NetYamlForge 实现了热重载监听机制（核心逻辑涉及 `YamlFileWatcher.cs` 和 `YamlHotReloadService.cs`）以及基于 `CollectibleAssemblyLoadContext` 的动态编译与卸载机制（核心逻辑位于 `ProjectHookLoader.cs`）。
- **JPCS 子项目实例**：JPCS 系统包含 project.yaml、dashboard.yml、实体 YAML 定义及初始数据库 jpcs.db，具备 33 个实体、7 个 Hook 文件、12 个自定义页面和 4 个核心批处理作业（包括合约过期提醒、月度自动请求生成等）。子项目主页模板涉及 `Index.cshtml` 和 `ProjectTemplateScaffolder.cs`。系统支持对未定义在 `entities.yml` 中的临时表（ad-hoc tables）使用轻量级降级机制。
- **数据库连接管理**：系统使用项目范围的数据库连接。推荐使用 `IConnectionManager` 统一管理数据库连接状态，替代在控制器（如重构后的 `UsersController`）中手动注入/打开 `IDbConnection` 的方式。必须使用 `ConnectionManager.GetConnectionAsync(projectName)` 来设置连接范围。
- **用户隔离与多租户认证**：
  - 认证（Authentication）通过主框架完成，但用户数据在子系统内部管理。采用“集中存储、逻辑隔离”的架构实现跨子项目的用户管理。子项目通过 `app_user` 表中的 `owning_project` 列实现用户隔离。
  - 涉及多租户身份验证（`UserAuthService`）、系统数据库初始化（`SystemDatabaseInitializer`，在应用启动时由 `DbInitializer` 调用 `InitializeAsync()`）和连接管理（`ConnectionManager`）的逻辑增强。`UserAuthService` 重构实现了 `IsAdmin` 与 `AppUserRole` 表的自动同步，并在用户名变更时更新角色映射。架构还包含租户服务 `ITenantUserService` 和 `AccountController`。
- **多语言资源管理**：NetYamlForge 项目的多语言资源通过共享资源包（SharedResource.resx，如 Localization.SharedResource.resx、zh-CN.resx 等 5 个资源文件）进行管理与扩展。
- **其他策略**：博客系统的公开页面无需身份认证，允许外部直接访问。

### 8. 部署与环境冲突
- **端口与冲突风险**：用户当前环境中可能同时运行着 AiChatApp 和 NetYamlForge，存在端口或域名指向混淆的风险。