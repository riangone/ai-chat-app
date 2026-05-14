---
name: security,bug,authorization,UserAuthService
description: UserAuthService 存在权限同步漏洞：AppUser 表的 IsAdmin 字段更新时未同步更新 AppUserRole 表，导致管理员权限检查不一...
type: user
userId: 1
tags: security,bug,authorization,UserAuthService
relations: UserAuthService,AppUser,AppUserRole
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:30:35.6911709Z
lastAccessedAt: 2026-05-12T23:30:35.6911710Z
---

UserAuthService 存在权限同步漏洞：AppUser 表的 IsAdmin 字段更新时未同步更新 AppUserRole 表，导致管理员权限检查不一致。