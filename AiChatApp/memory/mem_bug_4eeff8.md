---
name: bug, sql, blog, data-binding
description: 博客的“最新文章”区块因初始加载时缺少 SQL 命名参数（如 @category_id, @tag_id, @month）导致查询失败或返回空结果。
type: user
userId: 1
tags: bug, sql, blog, data-binding
relations: 最新文章,Home.yaml,Query Service
relevanceScore: 80
accessCount: 3
createdAt: 2026-05-15T02:22:41.8065728Z
lastAccessedAt: 2026-05-15T03:38:06.0039927Z
---

博客的“最新文章”区块因初始加载时缺少 SQL 命名参数（如 @category_id, @tag_id, @month）导致查询失败或返回空结果。