---
name: codebase,architecture,modules
description: AiChatApp 包含 TodoItem（任务管理）、LongTermMemory（长期记忆）、Skill（技能）和 ProactiveAgentProfil...
type: user
userId: 1
tags: codebase,architecture,modules
relations: AiChatApp,TodoItem,LongTermMemory,Skill,ProactiveAgentProfile,PromptVariant,PromptEvolutionService,AppDbContext,PipelineLoaderService
relevanceScore: 90
accessCount: 0
createdAt: 2026-06-14T01:37:33.1484693Z
lastAccessedAt: 2026-06-14T01:37:45.4266913Z
boundAgentRole: 
---

AiChatApp 包含 TodoItem（任务管理）、LongTermMemory（长期记忆）、Skill（技能）和 ProactiveAgentProfile（主动助理画像）等相关模块。此外，Loop 2 提示词演化的核心代码已开发完成且通过构建（但尚未提交），涉及新增的 PromptVariant 模型和 PromptEvolutionService 服务，以及对 AppDbContext、PipelineLoaderService 等现有文件的修改。