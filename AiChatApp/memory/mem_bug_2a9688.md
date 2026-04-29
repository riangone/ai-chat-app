---
name: bug,CooperateMode,AiService,Agent,SessionMemory
description: AiService.cs 中的 CooperateStreamAsync 方法目前是硬编码的，忽略了通过 chatSessionId 绑定的自定义 Agent ...
type: user
userId: 1
tags: bug,CooperateMode,AiService,Agent,SessionMemory
relevanceScore: 80
accessCount: 11
createdAt: 2026-04-28T23:21:32.2740460Z
lastAccessedAt: 2026-04-29T08:16:20.1514568Z
---

AiService.cs 中的 CooperateStreamAsync 方法目前是硬编码的，忽略了通过 chatSessionId 绑定的自定义 Agent 列表，导致流式协作模式下无法加载特定代理。