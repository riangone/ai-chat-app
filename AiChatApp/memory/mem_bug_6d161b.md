---
name: bug, root-cause, dapper, sql-binding
description: 用户反馈“最新文章”不显示数据。根因是 `PageDataQueryService.GetSectionDataAsync` 未对自定义 SQL 中的命名参数（...
type: user
userId: 1
tags: bug, root-cause, dapper, sql-binding
relations: PageDataQueryService,GetSectionDataAsync,Dapper
relevanceScore: 80
accessCount: 1
createdAt: 2026-05-15T03:40:20.9977124Z
lastAccessedAt: 2026-05-15T10:14:27.5318751Z
---

用户反馈“最新文章”不显示数据。根因是 `PageDataQueryService.GetSectionDataAsync` 未对自定义 SQL 中的命名参数（如 `@category_id`, `@tag_id`, `@month`）做兜底绑定，导致 Dapper 缺参抛异常并返回空数据。