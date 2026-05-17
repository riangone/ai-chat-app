---
name: performance, bug, AiChatApp, MemorySearchService
description: AiChatApp AI response performance is degraded by MemorySearchService.SearchAsync...
type: user
userId: 1
tags: performance, bug, AiChatApp, MemorySearchService
relations: AiChatApp,MemorySearchService,Performance
relevanceScore: 80
accessCount: 4
createdAt: 2026-05-14T23:17:14.6844031Z
lastAccessedAt: 2026-05-17T08:24:26.0619645Z
---

AiChatApp AI response performance is degraded by MemorySearchService.SearchAsync making hidden gemini CLI calls for semantic re-ordering when search results exceed 5 items.