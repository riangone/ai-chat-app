---
name: AiService, database, redundancy, P1
description: AiService.cs 的 RunAgentStepAsync 在多 Agent 协作时存在大量重复的数据库查询（项目根路径、技能搜索、文件搜索、Sessio...
type: user
userId: 1
tags: AiService, database, redundancy, P1
relations: AiService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-17T02:55:11.5348776Z
lastAccessedAt: 2026-05-17T02:55:11.5348777Z
---

AiService.cs 的 RunAgentStepAsync 在多 Agent 协作时存在大量重复的数据库查询（项目根路径、技能搜索、文件搜索、Session 记忆）。