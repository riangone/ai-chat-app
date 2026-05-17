---
name: AiService, 数据库优化, 性能瓶颈
description: AiService 中的 RunAgentStepAsync 存在冗余的数据库查询逻辑，包括重复查询项目根路径、技能列表和会话记忆。
type: user
userId: 1
tags: AiService, 数据库优化, 性能瓶颈
relations: AiService,RunAgentStepAsync
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-17T01:46:01.9273500Z
lastAccessedAt: 2026-05-17T01:46:01.9273501Z
---

AiService 中的 RunAgentStepAsync 存在冗余的数据库查询逻辑，包括重复查询项目根路径、技能列表和会话记忆。