---
name: framework,IEmbeddingService,DI,providers
description: IEmbeddingService 是框架核心的 Embedding 抽象接口，其具体实现（如 LmStudioEmbeddingService, Gemini...
type: user
userId: 1
tags: framework,IEmbeddingService,DI,providers
relations: IEmbeddingService,LmStudioEmbeddingService,GeminiEmbeddingService,LocalEmbeddingService
relevanceScore: 30
accessCount: 0
createdAt: 2026-06-20T11:46:52.4769153Z
lastAccessedAt: 2026-06-20T11:46:52.4769155Z
boundAgentRole: 
---

IEmbeddingService 是框架核心的 Embedding 抽象接口，其具体实现（如 LmStudioEmbeddingService, GeminiEmbeddingService, LocalEmbeddingService）由 DI 容器根据配置动态注入。