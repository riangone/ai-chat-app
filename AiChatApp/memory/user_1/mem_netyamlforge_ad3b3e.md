---
name: NetYamlForge,project progress,EVOLUTION_PLAN
description: # NetYamlForge 核心记忆汇总

## 一、 基本配置与部署信息
1. **本地路径与仓库**：本地克隆路径为 `/home/ubuntu/ws/N...
type: user
userId: 1
tags: NetYamlForge,project progress,EVOLUTION_PLAN
relations: NetYamlForge,EVOLUTION_PLAN,nyf branch,PostgreSQL schema-per-tenant,REST API,MCP Server,Schema Migration
relevanceScore: 90
accessCount: 28
createdAt: 2026-06-13T15:25:53.3515507Z
lastAccessedAt: 2026-06-14T11:08:41.0821166Z
boundAgentRole: 
---

# NetYamlForge 核心记忆汇总

## 一、 基本配置与部署信息
1. **本地路径与仓库**：本地克隆路径为 `/home/ubuntu/ws/NetYamlForge`，包含 16 个已配置的多租户项目目录。GitHub 仓库为 `git@github.com:riangone/NetYamlForge.git`。当前修改和提交已推送至 `nyf` 分支（即 `origin/nyf`），该分支已完成仓库卫生、CI 工作流、中日韩英四语本地化、SQLite 写锁根治、NoWarn 清理、端到端集成测试、`I18nText.Resolve` 可空修复，以及 EVOLUTION_PLAN Phase 4 的全部开发与部署集成工作。
2. **运行环境**：预期在 5001 端口以 Release 模式运行（以避免与 AiChatApp 冲突），但当前因默认配置在 5100 端口以 Debug 模式运行。日志存储在 `NetYamlForge/logs/`。
3. **数据库与租户管理**：系统采用中央 `system.db` 数据库与 `app_user` 表进行统一的租户管理与身份验证。同时，项目已完成 Phase 4.4 的 PostgreSQL schema-per-tenant 多租户架构演进，现有实现可提供参考。
4. **部署与路径修复**：项目部署于 `https://ai.0101.click/nyf`（基础路径 `/nyf`）。已实施一系列路径修复（包括 `Program.cs` 的 `UsePathBase` 与 `X-Forwarded-Prefix` 处理、`_Layout.cshtml` 注入 `window.NetYamlForgeConfig.pathBase`、动态 `@Url.Content` 路径替换及 `ai-assistant.js` 的 `apiBaseUrl` 和 `signalRUrl` 动态构造）以解决反向代理导致的路由和静态资源加载失效问题。
5. **服务启动修复**：通过先 `export` 环境变量再调用 `nohup` 的方式修复了 `restart.sh` 中的 Bash 启动缺陷，确保 dotnet 服务能 100% 成功拉起。

## 二、 架构、核心组件与待解决缺陷
1. **FSM 与槽位填充**：引入了 `IConversationFsm` 接口以解耦 FSM 硬编码缺陷；但 `SlotFillingManager.cs` 的状态（如 `_lowConfidenceCount`）仍保存在内存中未持久化，与数据库持久化的 `SlotSession` 槽位不同步。
2. **多租户连接管理**：存在高优先级的数据库多租户连接管理“双重池化”缺陷，涉及 `ConnectionManager.cs` 对每个租户项目维护自定义 of `ConnectionPool`，而物理连接上又强制附加了底层驱动的原生连接池参数。
3. **AI 编排**：`AiToolOrchestrator.cs` 中的 Tool 执行逻辑尚未实现，目前存在 TODO 标记。框架的核心功能之一是支持不同模型的 AI 分工合作来完成任务；在需要分工时，必须合理安排 AI 之间的协作与分工。
4. **编译阻碍问题**：项目当前编译受阻，具体原因是在测试文件 `OutboxJobQueueTests.cs` 中，对只读属性 `BatchJobResult.DurationMs` 进行了直接赋值，导致无法通过编译。

## 三、 架构重构、编译与安全性修复
1. **编译与重构**：解决了 `Services.Ai` 命名空间不一致问题，通过将 `BatchJobExecutor` 的构造函数参数减少至 5 个、使用 `IServiceProvider` 懒加载及引入 `IBatchStepHandler` 完成了解耦重构。
2. **视图编译修复**：在 `PageDefinition.cs` 的 `SectionDefinition` 类中添加了 `string? Class` 属性，修复了 `_SectionMarkdown.cshtml` 的 CS1061 编译错误。
3. **SQL 安全性**：SQL 标识符安全性已通过 `DynamicCrudRepository.ValidateMetadata` 的 `EnsureIdentifier` 校验，且其中的 `displayColumn` 仅支持简单物理列名。

## 四、 服务维护与故障修复
1. **页面与看板故障排查**：修复了上传文档页面（UploadPage）曾出现的“上传失败，请重试。”报错；同时通过修改 `DashboardController.cs` 修复了数据看板曾出现的“設定エラー”（配置错误）问题。
2. **国际化与多语言适配**：
   - **语言适配与测试**：系统的数据看板（Dashboard）和任务列表（DynamicEntity）已完成中、日、韩、英四种语言的全面适配与测试验证。
   - **语言切换失效修复**：修复了系统右上角菜单切换语言失效的问题。原因为重定向 URL（returnUrl）缺少 `/nyf` 前缀导致 Caddy 路由失效，已通过在布局文件中补全 `/nyf` 前缀进行修复。
3. **邮件服务与定时任务**：
   - **邮件服务**：基于 MailKit 实现了 SMTP 和 IMAP 服务（`IEmailService` 与 `MailKitEmailService`），支持通过 YAML 配置的 `SendEmailHook` 自动化触发发送；支持批处理任务失败时向 `notifyEmails` 发送错误预警邮件。
   - **邮件获取任务 (`email_fetch`)**：已实现定时任务轮询收件箱并将新邮件存入 `received_emails` 表。
   - **文档处理任务 (`ai_folder_processor_job`)**：已实现每 5 分钟运行一次的定时任务，用以处理上传文档。该任务监视的文件夹路径为 `NetYamlForge/wwwroot/watch-docs/ai-doc-processor/`，包含 `error/` 和 `processed/` 子目录，支持处理 `.pdf`、`.jpg`、`.png` 等格式的文档。

## 五、 子项目进展
1. **blog 子项目**：已完成全面升级，包含搜索、归档、标签云及“关于我”页面。
2. **Northwind Retail 演示项目**：已确认 15 个页面正常运行，页面访问 URL 格式为 `http://localhost:5100/nyf/northwind-retail/Page/<PageName>`。

## 六、 EVOLUTION_PLAN Phase 4 进展（已完成）
- NetYamlForge 项目的 EVOLUTION_PLAN Phase 4（包括 4.1 REST API、4.2 MCP Server、4.3 Schema 迁移、4.4 PostgreSQL schema-per-tenant 多租户）已全部开发测试完毕并推送至 `origin/nyf` 分支。

## 七、 后续规划与未来任务
项目的后续规划记录在 `docs/EVOLUTION_PLAN.md` 中。在 `nyf` 分支已完成历史警告清理和基础端到端测试的前提下，下一步可选任务包括：
- **Phase 2.1**：将端到端测试进一步扩充至六条核心路径。
- **Phase 3.2**：持久化任务队列 outbox。