---
name: architecture,db_management
description: UsersController 已重构，不再直接注入 IDbConnection，而是通过 IConnectionManager 管理数据库连接的获取与释放。
type: user
userId: 1
tags: architecture,db_management
relations: UsersController,IConnectionManager
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:41:27.8648121Z
lastAccessedAt: 2026-05-12T23:41:27.8648122Z
---

UsersController 已重构，不再直接注入 IDbConnection，而是通过 IConnectionManager 管理数据库连接的获取与释放。