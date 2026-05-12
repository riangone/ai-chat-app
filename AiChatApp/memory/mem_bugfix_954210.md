---
name: bugfix, architecture, database-scoping
description: 修复了 'No project scope set' 错误，UserAuthService 和 AuditLogService 已改为使用 GetConnect...
type: user
userId: 1
tags: bugfix, architecture, database-scoping
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T02:24:56.7606179Z
lastAccessedAt: 2026-05-12T02:24:56.7606180Z
---

修复了 'No project scope set' 错误，UserAuthService 和 AuditLogService 已改为使用 GetConnectionAsync(_scope.Current.Name) 命名重载。