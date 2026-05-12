---
name: architecture, initialization, database
description: `SystemDatabaseInitializer.InitializeAsync()` must be called during application ...
type: user
userId: 1
tags: architecture, initialization, database
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T02:09:29.6979112Z
lastAccessedAt: 2026-05-12T02:09:29.6979113Z
---

`SystemDatabaseInitializer.InitializeAsync()` must be called during application startup (within `DbInitializer`) to ensure the system database is correctly set up.