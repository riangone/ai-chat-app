---
name: backend, database, architecture
description: UsersController 进行了底层架构调整：将 IDbConnection 替换为 IConnectionManager 进行连接管理，并新增了用户删除...
type: user
userId: 1
tags: backend, database, architecture
relations: UsersController,IConnectionManager
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:51:34.4463092Z
lastAccessedAt: 2026-05-12T23:51:34.4463093Z
---

UsersController 进行了底层架构调整：将 IDbConnection 替换为 IConnectionManager 进行连接管理，并新增了用户删除（Delete）功能。