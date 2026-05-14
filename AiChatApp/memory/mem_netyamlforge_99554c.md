---
name: NetYamlForge, C#, YAML, validation, database, error-resolution
description: NetYamlForge 框架中的 `DynamicCrudRepository.ValidateMetadata` 会通过 `EnsureIdentifier...
type: user
userId: 1
tags: NetYamlForge, C#, YAML, validation, database, error-resolution
relations: DynamicCrudRepository,displayColumn,EnsureIdentifier
relevanceScore: 80
accessCount: 9
createdAt: 2026-05-13T01:50:02.3081988Z
lastAccessedAt: 2026-05-14T06:28:12.6044650Z
---

NetYamlForge 框架中的 `DynamicCrudRepository.ValidateMetadata` 会通过 `EnsureIdentifier` 检查标识符安全性。`displayColumn` 属性仅支持简单的列名（如 `LastName`），不支持 SQL 表达式（如 `LastName || ' ' || FirstName`），否则会触发 `InvalidOperationException: Unsafe identifier`。