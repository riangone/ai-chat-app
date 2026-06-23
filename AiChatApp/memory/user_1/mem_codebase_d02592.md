---
name: codebase,architecture
description: ### 1. NetYamlForge 项目
- `PageController.cs` 控制器中包含 `AnnotatePhoto` 和 `EmbedPhot...
type: user
userId: 1
tags: codebase,architecture
relations: NetYamlForge,AiChatApp,PageController,AiService,CliExecutorService,AiPromptService,LocalEmbeddingService,EmbeddingGeneratorExecutor,PromptVariant,PromptEvolutionService,AppDbContext,PipelineLoaderService,PhotoAnnotatorExecutor
relevanceScore: 90
accessCount: 68
createdAt: 2026-06-20T08:05:11.7465446Z
lastAccessedAt: 2026-06-23T14:45:49.0867011Z
boundAgentRole: 
---

### 1. NetYamlForge 项目
- `PageController.cs` 控制器中包含 `AnnotatePhoto` 和 `EmbedPhoto` 两个端点。

### 2. AiChatApp 项目
- **核心模块与架构**：包含任务管理 (`TodoItem`)、长期记忆 (`LongTermMemory`)、技能 (`Skill`) 和主动助理画像 (`ProactiveAgentProfile`) 等核心模块。
- **AI 调用与处理逻辑**：
  - [AiService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/AiService.cs) 中的 `GetResponseAsync` 和 `GetResponseStreamAsync` 方法负责处理 AI 调用及流式处理。
  - [CliExecutorService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/Infrastructure/CliExecutorService.cs) 中的 `SetupProcessInfo` 用于设置执行进程信息。
  - [AiPromptService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/AiPromptService.cs) 中的 `BuildSystemPromptAsync` 用于构建系统提示词。
- **内容过滤机制**：系统中实现了正则表达式及流式过滤逻辑，用于过滤并剥离“工作总结”和“WorkSummary”内容。相关实现位于 [CliExecutorService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/Infrastructure/CliExecutorService.cs) 的 `CleanResponse` 方法和 [AiService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/AiService.cs) 的 `WorkSummaryStreamFilter` 中。
- **本地向量服务优化**：
  - 服务端：在 `local_embed_server.py` 中新增 `POST /embed_batch` 接口以支持批量向量化。
  - 客户端：更新了 `LocalEmbeddingService.cs` 与 `EmbeddingGeneratorExecutor.cs` 以降低模型加载开销。
- **Loop 2 提示词演化**：核心代码已开发完成并通过构建（目前尚未提交），改动涉及：
  - 新增 [PromptVariant.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Models/Harness/PromptVariant.cs) 模型和 [PromptEvolutionService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/Harness/PromptEvolutionService.cs) 服务。
  - 修改现有文件，包括 [AppDbContext.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Data/AppDbContext.cs) 和 [PipelineLoaderService.cs](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/Harness/PipelineLoaderService.cs)。
- **外部服务清理**：已移除 `PhotoAnnotatorExecutor.cs` 中对 Anthropic/Claude 的相关实现。