---
name: implementation,refactoring,crud,delegation
description: Unified CRUD logic by modifying PageRowMutationService to delegate operations to...
type: user
userId: 1
tags: implementation,refactoring,crud,delegation
relations: PageRowMutationService,DynamicEntityCommandService,entities.yml
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T06:14:57.0316598Z
lastAccessedAt: 2026-05-14T06:14:57.0316599Z
---

Unified CRUD logic by modifying PageRowMutationService to delegate operations to DynamicEntityCommandService when a target_table is defined in entities.yml.