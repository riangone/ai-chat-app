---
name: 技术细节,排序逻辑,PageDataQueryService
description: 在 PageDataQueryService.cs 的分页查询生成逻辑中，外层包装会默认采用第一个字段升序排序并覆盖内部 SQL 的 ORDER BY，需在 H...
type: user
userId: 1
tags: 技术细节,排序逻辑,PageDataQueryService
relations: PageDataQueryService.cs,HomePage.yaml,diary_list
relevanceScore: 74
accessCount: 1
createdAt: 2026-06-29T06:34:47.6896202Z
lastAccessedAt: 2026-06-30T02:10:39.4733541Z
boundAgentRole: 
---

在 PageDataQueryService.cs 的分页查询生成逻辑中，外层包装会默认采用第一个字段升序排序并覆盖内部 SQL 的 ORDER BY，需在 HomePage.yaml 的组件配置中显式添加 defaultSort: CreatedAt 和 defaultSortDir: desc 来强制按时间降序排列。