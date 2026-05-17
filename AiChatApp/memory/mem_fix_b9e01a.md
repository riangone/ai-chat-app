---
name: fix, coding-standard, sql-logic
description: 修复方案：在 `GetSectionDataAsync` 中扫描自定义 SQL 的 `@param` 引用，并将未绑定的参数显式设置为 `null`。这确保了 ...
type: user
userId: 1
tags: fix, coding-standard, sql-logic
relations: PageDataQueryService,GetSectionDataAsync
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T03:40:21.0013520Z
lastAccessedAt: 2026-05-15T03:40:21.0013521Z
---

修复方案：在 `GetSectionDataAsync` 中扫描自定义 SQL 的 `@param` 引用，并将未绑定的参数显式设置为 `null`。这确保了 SQL 中的短路逻辑（如 `@category_id IS NULL OR ...`）在没有过滤器时能正确执行。