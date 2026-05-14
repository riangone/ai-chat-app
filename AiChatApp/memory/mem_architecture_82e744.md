---
name: architecture,best_practice,database,IConnectionManager
description: 项目推荐使用 IConnectionManager 统一管理数据库连接状态，以替代在控制器（如 UsersController）中手动注入和打开 IDbConn...
type: user
userId: 1
tags: architecture,best_practice,database,IConnectionManager
relations: UsersController,IConnectionManager
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:30:35.8080520Z
lastAccessedAt: 2026-05-12T23:30:35.8080521Z
---

项目推荐使用 IConnectionManager 统一管理数据库连接状态，以替代在控制器（如 UsersController）中手动注入和打开 IDbConnection 的模式。