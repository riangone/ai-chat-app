---
name: 缺陷修复,404,路由
description: 修复了导致 404 错误的 URL 路径问题：'/nyf/DynamicEntity?entity=jp_bill' 因缺少项目名 'jpcs' 和 Actio...
type: user
userId: 1
tags: 缺陷修复,404,路由
relations: jp_bill,jpcs,DynamicEntity
relevanceScore: 80
accessCount: 3
createdAt: 2026-05-14T15:33:23.5200530Z
lastAccessedAt: 2026-05-17T06:05:02.3844771Z
---

修复了导致 404 错误的 URL 路径问题：'/nyf/DynamicEntity?entity=jp_bill' 因缺少项目名 'jpcs' 和 Action 'Index' 而失效，正确路径应为 '/nyf/jpcs/DynamicEntity/Index?entity=jp_bill'。