---
name: security,standardization
description: BatchJobController 的鉴权方式从手动调用 UserIsAdmin() 统一为使用 [Authorize(Policy = "AdminOnly...
type: user
userId: 1
tags: security,standardization
relations: BatchJobController
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:41:27.8253363Z
lastAccessedAt: 2026-05-12T23:41:27.8253364Z
---

BatchJobController 的鉴权方式从手动调用 UserIsAdmin() 统一为使用 [Authorize(Policy = "AdminOnly")] 特性。