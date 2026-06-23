---
name: worker,queue_priority,annotation
description: 点击“立即标注”后，Worker 启动并会从队列中取出优先级（priority）为 10 的照片，并调用 `antigravity_cli_worker` 开始...
type: user
userId: 1
tags: worker,queue_priority,annotation
relations: Worker,processing_queue,antigravity_cli_worker
relevanceScore: 63
accessCount: 0
createdAt: 2026-06-21T11:48:40.2466568Z
lastAccessedAt: 2026-06-21T11:48:40.2466568Z
boundAgentRole: 
---

点击“立即标注”后，Worker 启动并会从队列中取出优先级（priority）为 10 的照片，并调用 `antigravity_cli_worker` 开始标注任务。