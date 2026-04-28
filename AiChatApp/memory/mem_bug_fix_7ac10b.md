---
name: bug-fix,AiService,session-memory
description: 从 AiService.cs 的 promptPrefixes 列表中移除了 'Memory:'，以防止误删 AI 生成的用于提取记忆的 'MEMORY:' 标...
type: user
userId: 1
tags: bug-fix,AiService,session-memory
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-28T23:19:08.6081672Z
lastAccessedAt: 2026-04-28T23:19:08.6081673Z
---

从 AiService.cs 的 promptPrefixes 列表中移除了 'Memory:'，以防止误删 AI 生成的用于提取记忆的 'MEMORY:' 标记。