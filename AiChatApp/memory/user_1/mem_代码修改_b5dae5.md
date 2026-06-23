---
name: 代码修改,PhotoAnnotationHandlers,annotate_now
description: 本主题下的代码修改主要涉及以下两个方面：

1. **图片标注功能实现**：
   - **处理器新增**：在 `PhotoAnnotationHandlers...
type: user
userId: 1
tags: 代码修改,PhotoAnnotationHandlers,annotate_now
relations: PhotoAnnotationHandlers.cs,AnnotateNowHandler,PhotoAnnotatorExecutor.cs,AnnotateSingleAsync,TableDdlBuilder.cs,DynamicEntitySchemaMigrationService.cs,IsDeleted
relevanceScore: 90
accessCount: 66
createdAt: 2026-06-19T13:00:22.2009803Z
lastAccessedAt: 2026-06-23T14:45:49.0867016Z
boundAgentRole: 
---

本主题下的代码修改主要涉及以下两个方面：

1. **图片标注功能实现**：
   - **处理器新增**：在 `PhotoAnnotationHandlers.cs` 中新增了 `AnnotateNowHandler`（注册为 `annotate_now` 处理器），该处理器通过依赖注入调用 `PhotoAnnotatorExecutor` 并将标注结果返回给前端。
   - **执行器新增**：在 `PhotoAnnotatorExecutor.cs` 中新增了 `public` 类型的 `AnnotateSingleAsync` 方法，实现从 `photos` 表读取文件路径，并结合 `project_settings` 中配置的 AI 标注服务商（如 LM Studio / Gemini / Ollama）进行单张图片标注，其标注结果（tags、embedding）在数据库事务内写回 `photos` 表。

2. **软删除（softDelete）实体及迁移逻辑优化**：
   - **DDL 构建优化**：修改了 `TableDdlBuilder.cs`，确保所有 `softDelete: true` 的实体在构建 DDL 时都会自动创建 `IsDeleted` 列。
   - **迁移服务优化**：修改了 `DynamicEntitySchemaMigrationService.cs` 中的 `BuildPlan` 方法，使其能够检测已有数据库表中是否缺失 `IsDeleted` 列，并在缺失时自动追加 `AddColumn` 迁移操作，防止其他软删除实体在运行时发生类似报错。