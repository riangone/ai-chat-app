---
name: bug-fix,AiService,backend
description: AiService.cs 中的 StripEchoedPromptPrefix 逻辑已被修复，解决了当回复内容与 'Assistant:' 前缀在同一行时回复内...
type: user
userId: 1
tags: bug-fix,AiService,backend
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-28T23:19:08.5978565Z
lastAccessedAt: 2026-04-28T23:19:08.5978565Z
---

AiService.cs 中的 StripEchoedPromptPrefix 逻辑已被修复，解决了当回复内容与 'Assistant:' 前缀在同一行时回复内容被误删的 Bug。