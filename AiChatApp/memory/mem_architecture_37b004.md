---
name: architecture,backend,ai-service,fallback,bug-context
description: The application's AI service uses a fallback mechanism (in CliExecutorService.cs...
type: user
userId: 1
tags: architecture,backend,ai-service,fallback,bug-context
relations: CliExecutorService,AiService,opencode,gemini,Fallback Mechanism
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T12:22:48.4328914Z
lastAccessedAt: 2026-05-25T12:22:48.4328915Z
---

The application's AI service uses a fallback mechanism (in CliExecutorService.cs and AiService.cs) that automatically calls the 'gemini' provider if the primary provider (like 'opencode') fails or returns empty, resulting in double CLI executions under failure conditions.