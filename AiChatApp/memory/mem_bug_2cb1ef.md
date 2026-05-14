---
name: bug, sql
description: PageDataQueryService 在注入用户上下文到 SQL 查询时，未包含 owningProject 参数，限制了页面级别的 YAML 查询进行项目...
type: user
userId: 1
tags: bug, sql
relations: PageDataQueryService,sql_injection
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T09:47:39.4238098Z
lastAccessedAt: 2026-05-14T09:47:39.4238099Z
---

PageDataQueryService 在注入用户上下文到 SQL 查询时，未包含 owningProject 参数，限制了页面级别的 YAML 查询进行项目过滤的能力。