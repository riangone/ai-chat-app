---
name: bug修复, 错误处理, DynamicEntityController, EntityMetadataProvider
description: 修复了 'DynamicEntityController' 中的一个严重 bug：当访问不存在的实体（例如 'purchase_order'）时，由于直接使用字...
type: user
userId: 1
tags: bug修复, 错误处理, DynamicEntityController, EntityMetadataProvider
relations: DynamicEntityController,EntityMetadataProvider,purchase_order
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T15:53:52.3599037Z
lastAccessedAt: 2026-05-14T15:53:52.3599039Z
---

修复了 'DynamicEntityController' 中的一个严重 bug：当访问不存在的实体（例如 'purchase_order'）时，由于直接使用字典索引导致抛出 KeyNotFoundException 并返回 500 错误。现已改为使用 TryGet 方法，并在实体不存在时返回 404 响应。