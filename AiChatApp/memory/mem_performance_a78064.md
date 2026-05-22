---
name: performance, stability, timeout
description: Added a 60-second timeout protection for AI generation processes in AiEmailChatE...
type: user
userId: 1
tags: performance, stability, timeout
relations: AiEmailChatExecutor.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-21T22:07:05.7994995Z
lastAccessedAt: 2026-05-21T22:07:05.7994996Z
---

Added a 60-second timeout protection for AI generation processes in AiEmailChatExecutor.cs to prevent blocking background task queues.