---
name: feature,AiToolOrchestrator,IToolRegistry,InMemoryToolRegistry
description: ### 1. 客户端、Chrome 扩展与交互功能 (AiChatApp, Hyperion, AiExtension & UI)
- **Chrome 扩展 ...
type: user
userId: 1
tags: feature,AiToolOrchestrator,IToolRegistry,InMemoryToolRegistry
relations: AiChatApp,Hyperion,AiExtension,AiService,AiToolOrchestrator,IToolRegistry,InMemoryToolRegistry,FinanceDataService,Sentinel Architecture,InvoiceEmailProcessorExecutor,WebPushService
relevanceScore: 90
accessCount: 220
createdAt: 2026-06-03T05:19:49.0256163Z
lastAccessedAt: 2026-06-14T11:08:41.0821169Z
boundAgentRole: 
---

### 1. 客户端、Chrome 扩展与交互功能 (AiChatApp, Hyperion, AiExtension & UI)
- **Chrome 扩展 (AiExtension)**：包含在页面显示 AI 助手、上传图片或文件自动输入，以及根据 AI 指示自动填写表单的功能（包含处理 WebForms 特性如 `ViewState`、`EventTarget` 的自动化填充逻辑，文件为 `content.js`）。后台通过 Native Messaging 调用 antigravity cli。
- **全局助手 Hyperion**：实现了一个名为 Hyperion 的全局 AI 助手侧边栏，支持在所有页面随时呼出进行自由对话。
- **输入框按钮整合**：AiChatApp 的输入框左侧按钮已整合为一个，点击可展示包含“文件上传”和“历史记录”的二级菜单，修改涉及 `wwwroot/components/footer.html` 和 `wwwroot/translations.js`。
- **流式传输与 Session**：支持流式与非流式聊天，前端 UI 增加了“Stream”模式切换开关；`ChatEndpoints.cs` 增加了对 `X-Session-Id` 响应头的支持。
- **交互与通知卡片**：实现了锁定屏幕跳转、向上拖动开启上下文面板、点击 AI Orb 开启聊天以及带 AI 动作的通知卡片。
- **系统摘要与锁屏 (Digest)**：系统支持通过数据库动态加载并生成摘要（Digest），涵盖紧急/近期记忆、任务以及最新聊天记录统计。相关功能模块包含 AI 摘要、任务进度、AI 记忆列表、学习卡片反馈（批准/删除）以及 7 种 AI 响应类型。
- **主题与视觉**：主题切换功能支持 `localStorage` 持久化，并对背景网格、装饰球体及手机壳原型视觉效果进行了优化。
- **核心交互模式**：包括主动推送今日推荐行动的“AI 朝礼”（Morning Briefing）和用于决策级互动的“AI 決定承認”（Decision Approval）。

### 2. AI 后端服务、多模态与工具执行 (AiService, OCR, Graph & Tool Execution)
- **工具执行优化 (Tool Execution)**：解决了 `AiToolOrchestrator` 空实现问题，新增 `IToolRegistry` 和 `InMemoryToolRegistry` 接口与实现，使 `AiToolOrchestrator` 可通过注册表查找并执行 Tool。
- **模型与参数重构**：`AiService.cs` 已重构，支持通过 `-m` 参数传递特定模型名称（如 `google/gemma-4-e2b`），并实现了 Vision 支持和 Prompt 剥离优化。
- **Vision 语法注入**：启用 Vision 引导语法自动注入功能，会在 Prompt 中的图像文件名前自动添加 `@` 前缀。
- **图像向量搜索**：通过 128 维向量实现，支持点击相机图标上传并实时预览缩略图。
- **OCR 识别**：`ImageOCRApp` 支持批量上传、本地路径扫描识别，并将结果自动保存为 `.txt` 文件。
- **记忆图谱 v2**：记忆图谱升级至 v2 版本 (Memory Graph v2)，修复了 Mermaid 拓扑渲染问题，支持长短期记忆的图形化索引。

### 3. 财务与数据同步服务 (Finance & Stock)
- **财务同步与比价**：通过 `FinanceDataService.cs` 实现了实时财务数据同步功能，利用 Gemini AI 抓取全球股票、基金及加密货币的价格；支持批量价格同步，并在添加新资产时自动进行单项比价。
- **持仓盈亏**：系统可根据持仓成本和最新价格，自动计算并显示持仓的市值与盈亏。
- **股票代码标准化**：AiChatApp 会自动将 4 位纯数字股票代码（如 9008）标准化，追加 `.T` 后缀以便通过 `yfinance` 查询东京证券交易所数据。
- **自动生成简报**：支持自动生成股票简报 (`china_stock_briefing`) 的任务，输出结果为 CSV 格式文件。

### 4. 系统核心架构与流管理 (Architecture & Agent Flow)
- **多代理协作流 (Multi-Agent Flow)**：支持定义不同角色（如 `API-Architect`, `UI-Specialist`）并在 `AgentSteps` 表中记录执行轨迹。
- **哨兵架构 (Sentinel Architecture)**：实现了哨兵架构 (Sentinel Architecture / `PROACTIVE_ENHANCEMENT_V2`)，通过监听 Git 增量变动并利用 `opencode` 引擎提供主动洞察。
- **动态页面实体逻辑**：通过 `DynamicEntityCommandService` 委托，将实体级逻辑（如 Hooks、验证、审计日志、软删除）自动应用于自定义页面；支持合并实体级 Hooks 和页面级 Hooks，以确保向后兼容和灵活的逻辑执行。
- **审计日志增强**：在 `PageRowMutationService` 中通过从 `PageController` 传递 `actorUserName` 来增强审计日志记录。
- **页面侧边栏自动列出**：在 `PageDefinition.cs` 中添加了 `ShowInSidebar` 属性，并更新了 `_Layout.cshtml` 模版。
- **工作流指南与导航**：为 `jpiere-cs` 项目实现了带有基于角色的步骤导航的工作流指南 UI；为 Mindmap 页面实现了面包屑导航（Breadcrumb Navigation）。
- **策略系统 (Harness)**：允许实时更新安全、风格和架构约束，并通过徽章状态反馈策略应用情况。

### 5. 工作区与业务处理 (Workspaces & Workflows)
- **工作区管理 (Workspaces)**：支持项目（Project）的列表查看、编辑和删除，可编辑属性包括标题（`Name`）和根路径（`Root Path`）。
- **邮件自动化**：实现了 `InvoiceEmailProcessorExecutor` (位于 `Services/BatchJob/`)，用于自动化接收 IMAP 邮件、通过 Gemini CLI 提取 JSON 信息、保存至数据库、生成 PDF 并发送回信。
- **Crew 功能**：项目中实现了 'Crew' 功能，包含 `CrewEndpoints.cs` 和 `Crew.cs` 模型。
- **附件管理系统**：支持将文件上传至 `/api/attachments/upload` 并通过 `attachmentIds` 与消息关联，包含后端服务 `AttachmentService.cs`、数据模型 `Attachment.cs` 和 API 端点 `AttachmentEndpoints.cs`。

### 6. 推送通知与 PWA (Push & PWA)
- **PWA 支持**：实现了 PWA 功能，包含 `manifest.json` 配置和 Meta 标签。
- **Web 推送系统**：实现了 Web Push 机制，包括 `WebPushService`、`ReminderService` 以及 PWA service worker (`sw.js`) 的支持。
- **推送配置与订阅**：PWA 推送通知支持需要启用 `appsettings.json` 中的 `ProactiveSettings:Enabled`。Service worker 由 `sw.js` 负责，前端订阅逻辑位于 `push-notifications.js`。

### 7. 认证与监控 (Auth & Monitoring)
- **用户认证**：采用 Cookie 会话管理；Python 环境下使用 `itsdangerous` 和 `passlib[bcrypt]`，默认账号密码为 `admin / admin123`。
- **状态监控**：支持监控系统 CPU、内存、运行时间、电池状态及发送通知，并支持数据库持久化。