---
name: architecture, database, connection-management
description: The application uses project-scoped database connections, and `ConnectionManager...
type: user
userId: 1
tags: architecture, database, connection-management
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T02:09:29.5970271Z
lastAccessedAt: 2026-05-12T02:09:29.5970272Z
---

The application uses project-scoped database connections, and `ConnectionManager.GetConnectionAsync(projectName)` must be used to set the scope.