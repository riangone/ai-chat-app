---
name: backend, auth, refactor
description: UserAuthService 进行了核心逻辑重构：实现了 IsAdmin 与 AppUserRole 的同步逻辑，支持随机密码生成，并统一使用 UTC 时间处...
type: user
userId: 1
tags: backend, auth, refactor
relations: UserAuthService,IUserAuthService,AppUserRole
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:51:34.4064092Z
lastAccessedAt: 2026-05-12T23:51:34.4064093Z
---

UserAuthService 进行了核心逻辑重构：实现了 IsAdmin 与 AppUserRole 的同步逻辑，支持随机密码生成，并统一使用 UTC 时间处理。同时，IUserAuthService 新增了 DeleteAsync 接口。