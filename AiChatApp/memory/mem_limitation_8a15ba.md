---
name: limitation,technical_debt,aiservice
description: 当前的 AiService.cs 在解析 CLI 输出时忽略了 usage 字段，导致 Token 使用量（Prompt/Completion Tokens）尚...
type: user
userId: 1
tags: limitation,technical_debt,aiservice
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-28T23:55:35.4144807Z
lastAccessedAt: 2026-04-28T23:55:35.4144808Z
---

当前的 AiService.cs 在解析 CLI 输出时忽略了 usage 字段，导致 Token 使用量（Prompt/Completion Tokens）尚未被提取和存储。