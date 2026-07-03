---
name: NetYamlForge,PostDetail.cshtml,yaml,highlight.js
description: ### NetYamlForge 框架核心记忆整合

#### 1. 框架定位与基础架构
- **定位**：低代码/声明式驱动应用框架 ([NetYamlFor...
type: user
userId: 1
tags: NetYamlForge,PostDetail.cshtml,yaml,highlight.js
relations: NetYamlForge,PostDetail.cshtml,highlight.js,yaml.min.js,FormValueValidationService.cs,DynamicEntitySchemaMigrationService.cs,PagePermissionService.cs,SlotFillingManager.cs,BatchJobPipeContext.cs,_SectionInteractiveForm.cshtml,PWA
relevanceScore: 90
accessCount: 14
createdAt: 2026-07-02T23:32:48.5146516Z
lastAccessedAt: 2026-07-03T05:06:21.9747523Z
boundAgentRole: 
---

### NetYamlForge 框架核心记忆整合

#### 1. 框架定位与基础架构
- **定位**：低代码/声明式驱动应用框架 ([NetYamlForge](file:///home/ubuntu/ws/NetYamlForge))。
- **项目路径**：`/home/ubuntu/ws/NetYamlForge`
- **核心服务与目录**：在 `Services` 目录下已包含的核心服务模块有 `Auth`（验证）、`Validation`（校验）、`Api`（接口）、`DynamicEntity`（动态实体）、`BatchJob`（批处理任务）、`AI`（AI核心服务）、`Connection`（连接系统），以及位于 `Services/Webhook` 和 `Services/Workflow` 的服务目录。
- **架构角色与分工**：
  - Assistant 扮演 Architect (架构师) 角色，进行底层基础功能的改进与进化。
  - 前期基础框架编码工作（如创建 [IWorkflowGuard.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Workflow/IWorkflowGuard.cs)）由子代理 `opencode` (会话ID `c88b3f18-a156-42f2-9ac6-41de5480e3fe`) 完成。
  - 随后由 `Hyperion` 接管，补全了核心逻辑和集成工作。

#### 2. 校验与验证服务 (Validation)
- **核心实现**：服务目录包含 [FormValueValidationService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/FormValueValidationService.cs) 和 [IFieldValidator.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Validation/IFieldValidator.cs) 验证接口。
- **高级校验器整合**：项目已整合高级字段校验器（`RegexFieldValidator`, `RangeFieldValidator`, `ConditionalFieldValidator`），解决了此前高级验证器设计缺失的痛点。
- **初始化与单元测试**：在 `FormValueValidationService.cs` 中通过构造函数自动初始化，以支持非 DI 实例化与单元测试，并在 `DynamicEntityFormValidationServiceTest` 中补充了测试用例。

#### 3. 声明式数据建模与自动迁移
- **核心实现**：由 [DynamicEntitySchemaMigrationService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/DynamicEntity/DynamicEntitySchemaMigrationService.cs)、[EntitySchemaWatcherService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/DynamicEntity/EntitySchemaWatcherService.cs) 以及 `RowMutationRepository` 共同支持。
- **主要功能**：支持通过 YAML/JSON 定义实体、字段类型、索引以及 1:1、1:N、N:M 等实体关联关系。系统能自动比对物理数据库 Schema 并生成及执行相应的 DDL 迁移，支持在不停机的情况下动态更新数据库结构（热更新）。
- **数据库支持与限制**：设计上支持 SQLite、PostgreSQL、MySQL 和 SQL Server 等数据库。但在当前实现中存在限制，对非 SQLite/PostgreSQL 方言会进行硬抛错。

#### 4. 权限与行级安全 (RLS)
- **核心实现**：主要在 [PagePermissionService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Auth/PagePermissionService.cs) 和 [DynamicCrudRepository.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/DynamicEntity/DynamicCrudRepository.cs) 中实现。
- **功能描述**：已实现细粒度的行级安全与权限控制。[PagePermissionService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Auth/PagePermissionService.cs) 实现基于角色的页面读写及字段写入过滤，[DynamicCrudRepository.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/DynamicEntity/DynamicCrudRepository.cs) 提供底层的数据过滤与行级安全支持。

#### 5. AI 核心模块 (Services/AI)
- **场景加载与热重载**：包含 AI 场景加载器 `AiScenarioYamlLoader` 和基于 `FileSystemWatcher` 的 YAML 热重载服务 `YamlHotReloadService`。该服务监听 [scenarios.yaml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/scenarios.yaml) 和 [scenarios.yml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/scenarios.yml) 的变动，通过 `IAiScenarioYamlLoader` 接口实现内存配置缓存的实时无缝热刷新。系统已实现配置热重载功能。
- **工具注册与验证**：使用 `IToolRegistry` 和 `ToolCallValidator` 进行 AI 工具的注册与调用验证。
- **状态机与会话管理**：
  - [SlotFillingManager.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/SlotFillingManager.cs) 负责管理有限状态机 (FSM) 会话的数据库读取与保存流程。
  - [AppointmentStateMachine.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/AppointmentStateMachine.cs) 用于状态机与连接设计。
  - **架构痛点**：AI 场景配置与内存有限状态机 (FSM) 存在高度硬编码问题，具体瓶颈定位在 [SlotFillingManager.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/AI/SlotFillingManager.cs) 中。

#### 6. 批处理任务模块 (Services/BatchJob)
- **后台与定时任务**：系统已实现通过 `BatchJob` 运行的后台任务及定时 Job 功能。
- **BatchJob 上下文管道**：创建了线程安全的容器 [BatchJobPipeContext.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/BatchJob/BatchJobPipeContext.cs)，用于规范 BatchJob DAG 步骤之间的输入输出上下文参数流转，并已在 [BatchJobDefinition.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/BatchJob/BatchJobDefinition.cs) 中完成集成。

#### 7. 底层增强特性设计与实现状态
- **实现状态澄清**：虽然 `Services/Webhook` 和 `Services/Workflow` 目录中已包含由 `opencode` 编写并由 `Hyperion` 接管集成的基础框架编码（如 [IWorkflowGuard.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Workflow/IWorkflowGuard.cs)），但**四项底层增强特性的具体逻辑代码实现尚未开始**。目前的提交仅为此前已完成的底层基础框架重构。
- **四项计划增强特性**：工作流引擎 (`IWorkflowEngine`)、动态速率限制中间件 (`DynamicRateLimitingMiddleware`)、Webhook 发件箱轮询器 (`WebhookOutboxPoller`) 和租户配额验证器 (`ITenantQuotaValidator`)。
- **设计缺失与痛点**：事件总线 (EventBus) 和 API 限流 (RateLimit) 目前面临设计缺失，正处于评估与分析阶段。
- **详细设计文档**：相关的增强详细设计文档已生成，保存在 [detailed_design_for_additional_framework_features.md](file:///home/ubuntu/ws/NetYamlForge/docs/detailed_design_for_additional_framework_features.md)，目前处于 Git 未追踪状态。
- **其他安全性与审计缺陷**：
  - [FileUploadService.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/FileUploadService.cs)（文件上传管理器）在处理二进制格式等方面存在审计缺陷或限制。
  - [ProjectHookLoader.cs](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Services/Project/ProjectHookLoader.cs) 动态编译钩子时允许 Unsafe 并缺失 AST 静态审查，存在安全风险。为了解决加载 Hook 时丢失库命名空间的问题，在 Roslyn 动态编译中显式引入了 `SixLabors.ImageSharp` 强引用。

#### 8. 前端交互、定位机制与缺陷修复
- **获取地理位置与逆解析**：日记系统在 [_SectionInteractiveForm.cshtml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionInteractiveForm.cshtml) 中通过 `navigator.geolocation` 获取设备经纬度，并使用 OpenStreetMap (OSM) Nominatim API 进行逆地理编码解析（解析格式为：`城市 · 社区 · 道路`）。解析结果存储于 `Location` 字段中，并作为 AI 情绪分析的上下文参数。
- **Lightbox 大图预览**：在 [_SectionInteractiveForm.cshtml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionInteractiveForm.cshtml)（用于表单上传图片预览）及 [_SectionFancyDiaryList.cshtml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionFancyDiaryList.cshtml)（用于日记卡片流和详情展示 Modal 中的图片）中，实现了带有磨砂玻璃背景与 `.animate-zoom-in` 缩放动效的 Lightbox 大图预览。
- **编辑回显缺陷修复**：解决了 DiaryForm 编辑时，由于 SQLite 底层返回字典键名大小写不一致（例如返回 'imagelabel'），导致标注图片内容（'ImageLabel'）、天气 and 心情无法回显的 bug。已通过在 [_SectionInteractiveForm.cshtml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/Page/Components/_SectionInteractiveForm.cshtml) 中实现不区分大小写的键值查找成功解决。
- **日记保存功能缺陷**：当前保存日记功能存在缺陷，在显示“心灵对话中”后迅速返回一览时，界面上无法显示图片且没有标注。
- **语法高亮与样式适配**：视图文件 [PostDetail.cshtml](file:///home/ubuntu/ws/NetYamlForge/NetYamlForge/Views/PostDetail.cshtml) 引入了 highlight.js 的 `yaml.min.js` 语言模块，以实现 yaml 和 yml 代码块的语法高亮与样式适配。

#### 9. PWA (渐进式 Web 应用) 支持与缺陷排查
- **PWA 实现**：框架目前正在实现 PWA 支持。
- **核心配置文件**：主要包含 `wwwroot/manifest.json` 和 `wwwroot/service-worker.js`。
- **离线提示页面**：已实现离线提示页面 `wwwroot/offline.html`。
- **缓存策略**：Service Worker 对核心静态资源采用 Stale-While-Revalidate 缓存策略，对 HTML 采用 Network-First 策略，并支持 CDN 动态缓存。
- **图标资源**：已在 `wwwroot/images` 目录下生成符合 PWA 标准的渐变 3D 立方体图标，包括 `icon-192.png`、`icon-512.png` 及其 maskable 版本。
- **当前故障与调试需求**：用户反馈重新编译后未收到安装到主屏幕的提示，且浏览器菜单中缺少安装选项。需要排查 `wwwroot` 目录下的 `manifest.json`、`service-worker.js` 以及预缓存文件目录（如 `images`、`css`、`lib` 目录）是否存在或配置正确。

#### 10. 演进与改进计划
项目包含以下演进与改进计划文档：
- [EVOLUTION_PLAN.md](file:///home/ubuntu/ws/NetYamlForge/EVOLUTION_PLAN.md)：长远进化规划，明确涵盖 API-first、MCP Server 以及 Schema 迁移系统。
- [IMPROVEMENT-PLAN-2026-06.md](file:///home/ubuntu/ws/NetYamlForge/IMPROVEMENT-PLAN-2026-06.md)：记录非 AI 模块底层基本功能的改进计划。
- [FRAMEWORK-IMPROVEMENTS-PLAN-V2.md](file:///home/ubuntu/ws/NetYamlForge/FRAMEWORK-IMPROVEMENTS-PLAN-V2.md)：记录 AI 与 Batch 设计的架构缺陷及相关痛点（如场景配置与内存有限状态机 FSM 存在的硬编码瓶颈）。