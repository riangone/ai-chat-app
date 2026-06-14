---
name: bug-fix,css,shadow-dom,daisyui
description: 已对“bug-fix”标签下的多项长期记忆进行了整合与去重，主要修复内容按模块/项目分类如下：

1. **Auto-Dealer-Demo 项目与详情页修复*...
type: user
userId: 1
tags: bug-fix,css,shadow-dom,daisyui
relations: Auto-Dealer-Demo,NetYamlForge,JPCS,AiChatApp,AI Assistant,DaisyUI,SQLite,HTMX,Nginx,Gemini CLI
relevanceScore: 87
accessCount: 233
createdAt: 2026-06-01T05:43:45.7634459Z
lastAccessedAt: 2026-06-14T11:08:41.0821171Z
boundAgentRole: 
---

已对“bug-fix”标签下的多项长期记忆进行了整合与去重，主要修复内容按模块/项目分类如下：

1. **Auto-Dealer-Demo 项目与详情页修复**
   - 修复了 AI 决定管理详情页（DetailPage.cshtml）点击“承认”（Approve）按钮时报“找不到アクションハンドラー 'approve_ai_decision'”的错误。根本原因是前端按钮指向了不存在的后端动作 `ExecuteAction`，现已修正为 `InvokeAction` 并统一了参数命名。

2. **NetYamlForge 项目（与 PathBase /nyf 相关）修复**
   - 修复了 POST Edit 操作时 AntiForgery 验证失败的问题。通过在 `NetYamlForge/Views/DynamicEntity/_Form.cshtml` 的页面模式和模态框模式中各添加 `@Html.AntiForgeryToken()` 解决。
   - 修复了 PageController 中的 `AntiforgeryValidationException`。根本原因为项目 PathBase 设置为 `/nyf` 导致 CSRF Cookie 路径不匹配，已将 Antiforgery Cookie 路径显式设置为 `/`。
   - 解决了子项目导航链接中因硬编码缺少 `/nyf` 前缀导致登录重定向（Auth Cookie 未发送）的问题。

3. **JPCS 项目与配置修复**
   - 修复了因执行 `ALTER TABLE` 导致已有用户的 `owning_project` 字段为 NULL 的问题，已将 `jpcs_admin` 和 `jpcs_user1` 正确归属于 `jpcs` 项目。
   - 修复了 `projects/jpcs/dashboard.yml` 中的 schema 验证错误，将不支持的 `entity-list` 图表类型替换为 `bar` 图表。

4. **AI 助手与 AiChatApp (Gemini CLI / Stream / 邮件) 相关修复**
   - 修复了点击打开 AI 助手面板无反应的 Bug。原因为 Shadow DOM 内 fixed 定位受 DaisyUI 布局干扰，通过调整 Shadow Host 为 100vw/100vh 并将面板/按钮改为 absolute 定位及配置 pointer-events 解决。
   - 修复了 AI 邮件聊天系统中 Gemini CLI 调用的超时问题。根本原因是 `AiEmailChatExecutor.cs` 中未设置 `RedirectStandardInput = true` 并手动关闭 stdin，导致 CLI 在非交互模式下无限等待输入。
   - 修复了前端聊天面板在中文环境下的显示问题，已为默认欢迎词添加 `data-i18n` 属性。
   - 重新启用了 `ProactiveAgentHub.cs` 中的 Welcome Insight 生成功能，以确保用户连接时能收到主动欢迎消息。
   - 修复了 Home 页面的欢迎简报显示问题，将 `BriefingEndpoints.cs` 中 gemini CLI 的 `outputFormat` 从 `'plain'` 修改为 `'text'`。
   - 修复了 Streaming Error: 405 错误，将后端 `/api/chat/stream` 接口从 GET 改为 POST，并修正了 Nginx 对 `/gennai/` 路径前缀的转发配置。
   - 修复了 AiChatApp 中严重的 DI 作用域异常：当 `UserAuthService` 和 `AuditLogService` 通过 `IServiceScopeFactory` 创建新作用域时，因 Scoped 生命周期的 `ProjectScope` 未经过中间件初始化，导致 `IsSet` 为 false 并抛出异常。
   - 修复了 `UserAuthService` 错误地调用了无参数 `GetConnectionAsync()` 重载的问题。

5. **数据库、配置文件与通用交互修复**
   - 优化了 `.env` 配置文件解析器，使其能够正确处理行尾注释（如 `AI_MODEL= # Comment`），修复了因模型名称解析错误导致的 `ModelNotFoundError`。
   - 修复了 `DynamicCrudRepository.cs` 中关于复合主键（Composite Primary Key）的 Bug。根本原因是 `GetByIdAsync` 在处理复合键时未正确绑定 `@Id0`, `@Id1` 等参数。
   - 解决了用户登录时因 Seeder 文件中密码哈希算法不一致（部分使用 SHA256 而非标准的 BCrypt）导致提示“ID或密码错误”的问题。
   - 修复了 SQLite 报 `no such column: is_admin` 的错误，该错误指示系统数据库（`system.db`）未被正确初始化。
   - 修复了 Financial Management 模块中资产添加无响应的问题。后端 `FinanceEndpoints.cs` 增加了 try-catch 异常处理和日志记录，前端通过 HTMX 返回错误信息提示。
   - 修复了 HTMX 列表页面在异步加载后导致浏览器后退键失效（需点击两次）的 Bug。
   - 修复了 `PostDetail.yaml` 中标题不渲染的问题。通过修改其 SQL 查询，在 HTML 头部（`</div>`）与正文内容之间插入了两个换行符 (`CHAR(10) || CHAR(10)`)。