---
name: implementation,refactoring,crud,delegation
description: Unified CRUD logic by modifying PageRowMutationService to delegate operations to...
type: user
userId: 1
tags: implementation,refactoring,crud,delegation
relations: PageRowMutationService,DynamicEntityCommandService,entities.yml
relevanceScore: 80
accessCount: 1
createdAt: 2026-05-14T06:14:57.0316598Z
lastAccessedAt: 2026-05-14T23:28:56.2015069Z
---

Unified CRUD logic by modifying PageRowMutationService to delegate operations to DynamicEntityCommandService when a target_table is defined in entities.yml.