---
name: performance, architecture, AiChatApp
description: In AiChatApp's collaborative mode, BuildSystemPromptAsync is called up to 3 time...
type: user
userId: 1
tags: performance, architecture, AiChatApp
relations: AiChatApp,BuildSystemPromptAsync,Collaborative Mode
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T23:17:14.7314519Z
lastAccessedAt: 2026-05-14T23:17:14.7314520Z
---

In AiChatApp's collaborative mode, BuildSystemPromptAsync is called up to 3 times, multiplying the hidden CLI call overhead.