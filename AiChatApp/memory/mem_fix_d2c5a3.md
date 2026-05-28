---
name: fix, routing, yaml, auto-dealer-demo
description: 在 auto-dealer-demo 项目中，将 YAML 配置文件里的 /DynamicEntity/Edit 和 /DynamicEntity/Detail...
type: user
userId: 1
tags: fix, routing, yaml, auto-dealer-demo
relations: auto-dealer-demo,DynamicEntity
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-28T07:30:34.0946737Z
lastAccessedAt: 2026-05-28T07:30:34.0946738Z
---

在 auto-dealer-demo 项目中，将 YAML 配置文件里的 /DynamicEntity/Edit 和 /DynamicEntity/Detail 批量修正为 /DynamicEntity/EditPage 和 /DynamicEntity/DetailPage，解决了路由名称不匹配导致的 405/404 错误。