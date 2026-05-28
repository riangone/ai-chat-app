---
name: backend-optimization, HTMX, redirection, DynamicEntityController
description: 优化了 DynamicEntityController.cs 中的 InvokeAction 方法逻辑，使其能够根据请求类型（HTMX 或标准表单）自动处理返回...
type: user
userId: 1
tags: backend-optimization, HTMX, redirection, DynamicEntityController
relations: DynamicEntityController,HTMX,TempData
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-28T08:42:56.4413611Z
lastAccessedAt: 2026-05-28T08:42:56.4413612Z
---

优化了 DynamicEntityController.cs 中的 InvokeAction 方法逻辑，使其能够根据请求类型（HTMX 或标准表单）自动处理返回局部视图或重定向，并利用 TempData 传递操作结果状态。