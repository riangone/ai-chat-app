---
name: performance,AiService,caching
description: A 5-minute static cache was added to LoadPoliciesAsync in AiService to eliminate...
type: user
userId: 1
tags: performance,AiService,caching
relations: AiChatApp,AiService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T23:22:12.8545098Z
lastAccessedAt: 2026-05-14T23:22:12.8545098Z
---

A 5-minute static cache was added to LoadPoliciesAsync in AiService to eliminate repeated file I/O for policy loading.