---
name: bug-fix, DynamicCrudRepository, composite-key
description: 在 DynamicCrudRepository.cs 中修复了一个关于复合主键（Composite Primary Key）的 Bug。原因为 GetByIdA...
type: user
userId: 1
tags: bug-fix, DynamicCrudRepository, composite-key
relations: DynamicCrudRepository,GetByIdAsync,SQLite
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-13T03:35:32.9606182Z
lastAccessedAt: 2026-05-13T03:35:32.9606183Z
---

在 DynamicCrudRepository.cs 中修复了一个关于复合主键（Composite Primary Key）的 Bug。原因为 GetByIdAsync 在处理复合键时未正确绑定 @Id0, @Id1 等参数。