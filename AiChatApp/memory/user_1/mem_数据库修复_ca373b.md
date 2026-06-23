---
name: 数据库修复,antigravity,processing_queue,antigravity_cli_worker
description: 通过直接在数据库中将故障照片的 `processing_queue` 条目删除并重新插入 `provider='antigravity'` 且 `priorit...
type: user
userId: 1
tags: 数据库修复,antigravity,processing_queue,antigravity_cli_worker
relations: processing_queue,antigravity_cli_worker,antigravity
relevanceScore: 63
accessCount: 0
createdAt: 2026-06-21T12:46:11.7755396Z
lastAccessedAt: 2026-06-21T12:46:11.7755397Z
boundAgentRole: 
---

通过直接在数据库中将故障照片的 `processing_queue` 条目删除并重新插入 `provider='antigravity'` 且 `priority=10` 的新条目完成修复，因为 `antigravity_cli_worker` 是启用状态且每 3 分钟运行一次。