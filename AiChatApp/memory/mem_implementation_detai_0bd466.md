---
name: implementation-detail, csharp, service-layer
description: 在 DynamicEntityListHttpResponseService 中实现了 TrySetPushUrl 逻辑，通过判断 HX-Trigger 和 H...
type: user
userId: 1
tags: implementation-detail, csharp, service-layer
relations: DynamicEntityListHttpResponseService,DynamicEntityController
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T01:44:51.2084771Z
lastAccessedAt: 2026-05-14T01:44:51.2084771Z
---

在 DynamicEntityListHttpResponseService 中实现了 TrySetPushUrl 逻辑，通过判断 HX-Trigger 和 HX-Current-Url 来智能决定历史记录推送策略。