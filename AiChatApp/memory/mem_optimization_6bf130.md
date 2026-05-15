---
name: optimization, caching, AiChatApp
description: LoadPoliciesAsync in AiChatApp reads policy files from disk without caching, cau...
type: user
userId: 1
tags: optimization, caching, AiChatApp
relations: AiChatApp,LoadPoliciesAsync,Caching
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T23:17:14.8258493Z
lastAccessedAt: 2026-05-14T23:17:14.8258493Z
---

LoadPoliciesAsync in AiChatApp reads policy files from disk without caching, causing unnecessary I/O on every request.