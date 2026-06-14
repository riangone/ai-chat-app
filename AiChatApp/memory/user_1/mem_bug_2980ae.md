---
name: bug,localization,ui,language-switching
description: ### 错误与缺陷列表

#### 一、 本地化与多语言缺陷
1. **多语言切换失效**：用户在右上角菜单点击切换语言时，界面语言没有发生变化，一直显示为中文...
type: user
userId: 1
tags: bug,localization,ui,language-switching
relations: LocalizationController,UserPreferredLanguageProvider,BatchJobExecutor,AiFolderProcessorExecutor,AiChatApp,AiService,NetYamlForge,PageDataQueryService,TenantAccountController,ProjectScopeMiddleware,DynamicEntityController,UsersController
relevanceScore: 90
accessCount: 67
createdAt: 2026-06-09T12:33:51.5620708Z
lastAccessedAt: 2026-06-14T11:08:41.0821169Z
boundAgentRole: 
---

### 错误与缺陷列表

#### 一、 本地化与多语言缺陷
1. **多语言切换失效**：用户在右上角菜单点击切换语言时，界面语言没有发生变化，一直显示为中文。目前正在排查 `LocalizationController` 和 `UserPreferredLanguageProvider` 的实现。

#### 二、 数据库与后台服务并发缺陷
1. **SQLite 数据库写锁冲突**：`BatchJobExecutor` 和 `AiFolderProcessorExecutor` 曾因 SQLite 数据库写锁冲突（错误码 SQLite Error 5: 'database is locked'）导致定时任务执行失败。

#### 三、 AiChatApp 与 AI 服务相关缺陷
1. **输入框交互缺陷**：在 AiChatApp 中，当输入框内文字过多时，输入框会占满整个屏幕，导致用户无法进行其他操作。
2. **界面卡死与冻结问题**：
   - 当切换到 Mindmap 页面时，页面会出现冻结（卡死）的情况。
   - 在进行图片文字识别时，界面会卡在 'Gemma 4 正在识别图片文字...'，后台提示 gemini CLI 因缺少 pgrep 输出而报错。
3. **系统提示词泄露（System Prompt Leak）**：
   - **表现**：AI 代理在每次回复时会在开头回显系统提示词（例如 Hyperion 的身份设定）。
   - **原因**：`AiService.cs` 中过滤片段不完整，且流式响应缺乏过滤逻辑。
   - **解决方案**：(1) 完善 `SystemPromptFragments` 静态列表以精准识别系统提示词片段；(2) 重构 `GetResponseStreamAsync` 方法，引入最大 4096 字符的前缀缓冲区，在流式输出开始前自动过滤回显的系统指令。
4. **流式协作模式硬编码**：`AiService.cs` 中的 `CooperateStreamAsync` 方法目前是硬编码的，忽略了通过 `chatSessionId` 绑定的自定义 Agent 列表，导致流式协作模式下无法加载特定代理。

#### 四、 NetYamlForge 项目 - 权限、隔离与多租户缺陷
1. **“最新文章”部分无法显示数据**：
   - **表现**：`/nyf/blog/Page/Home` 页面的“最新文章”模块未显示数据，加载失败。
   - **原因**：`PageDataQueryService.GetSectionDataAsync` 在初始加载时未对自定义 SQL 中的命名参数（如 `@category_id`, `@tag_id`, `@month`）做兜底绑定，导致 Dapper 缺参抛异常并返回空结果。
2. **子项目用户管理过滤失效**：子项目（如 `/jpcs/Users`）的用户管理页面未能按项目自动过滤用户，仍显示全局所有用户，偏离了仅显示该子项目用户的预期行为。
3. **缺少 owningProject 项目过滤参数**：`PageDataQueryService` 注入用户上下文到 SQL 查询时未包含 `owningProject` 参数，限制了页面级别的 YAML 查询进行项目过滤的能力。
4. **用户登录 Claim 缺失**：`TenantAccountController` 的登录流程中缺失了设置 `owning_project` 声明（Claim）的逻辑，导致多租户登录用户无法正确关联项目。
5. **跨项目访问控制中间件未注册**：`ProjectScopeMiddleware` 虽然已定义，但在 `Program.cs` 中未被注册，导致缺乏跨项目的访问控制。
6. **静态资源（JS/CSS）路径缺少 `/nyf` 前缀导致加载失败**：
   - **原因**：Caddy 使用 `handle_path /nyf*` 剥离了 `/nyf` 前缀后转发请求，导致 ASP.NET Core 的 `PathBase` 为空，`~/` 占位符和 `@Url.Content` 无法生成含 `/nyf` 的路径（如 `daisyui.min.css` 应该包含该前缀）。
   - **解决方案**：
     1) 在 `Program.cs` 中添加兜底中间件，若 `PathBase` 为空则强制设为 `/nyf`；
     2) 在 Razor 视图中，将所有 `/lib/...` 硬编码路径改为 `@Url.Content('~/lib/...')`；
     3) 在 `window.NetYamlForgeConfig.pathBase` 中使用 `@Url.Content('~/').TrimEnd('/')` 动态传递路径前缀给前端 JS；
     4) 更新 `ProjectTemplateScaffolder.cs` 模板，确保新生成的子项目也具备相同的路径处理；
     5) 若问题持续，可在 Caddy 中改用 `handle`（保留前缀）而非 `handle_path`（剥离前缀）。

#### 五、 NetYamlForge 项目 - 数据库、控制器与视图配置缺陷
1. **CSS 隔离 404 错误**：NetYamlForge 项目存在已知的 404 错误，涉及 `/NetYamlForge.styles.css` 文件（Blazor/Razor CSS 隔离问题）。
2. **缺少防伪令牌错误**：在执行 `DynamicEntityController.Edit` 的 POST 请求时遇到了 `AntiforgeryValidationException` 错误，原因是缺少防伪令牌。
3. **数据库相关错误**：
   - **标识符不安全异常**：由于表名 'Order Details' 包含空格，导致抛出 `InvalidOperationException: Unsafe identifier` 异常。
   - **数据库列缺失**：SQLite 数据库中缺少 'is_admin' 列，导致登录时出现 SQLite Error 1 并触发 500 错误。
4. **布局视图定位失败（500 错误）**：
   - **原因**：`_ViewStart.cshtml` 中配置的绝对路径导致 `IFileProvider` 无法正确解析布局视图。
   - **表现**：项目 'Northwind Retail' 遇到 500 错误，原因是无法定位布局视图 `/projects/northwind-retail/views/_Layout.cshtml`。
5. **删除操作未实现**：`UsersController` 目前缺少 `Delete` 操作实现，可能导致前端视图链接出现 404。
6. **模式验证失败**：项目启动时遇到模式验证失败，原因为 `jpcs/entities/` 目录下 1148 个实体 YAML 文件使用了不兼容的 iDempiere 导出格式。