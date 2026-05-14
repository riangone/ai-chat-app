---
name: fix,NetYamlForge,SQLite
description: 在 EntityDbSchemaConsistencyValidator.cs 的 LoadRequiredInputColumnsSqlite 方法中增加了剥...
type: user
userId: 1
tags: fix,NetYamlForge,SQLite
relations: EntityDbSchemaConsistencyValidator,NetYamlForge
relevanceScore: 80
accessCount: 1
createdAt: 2026-05-13T03:25:36.3318090Z
lastAccessedAt: 2026-05-13T03:31:17.7593402Z
---

在 EntityDbSchemaConsistencyValidator.cs 的 LoadRequiredInputColumnsSqlite 方法中增加了剥离方括号的逻辑，以防止出现双重方括号（如 [[Order Details]]）的情况。