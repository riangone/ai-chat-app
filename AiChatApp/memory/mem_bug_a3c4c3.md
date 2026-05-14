---
name: bug, authentication
description: TenantAccountController 的登录流程中缺失了设置 owning_project 声明（Claim）的逻辑，导致多租户登录用户无法正确关联项...
type: user
userId: 1
tags: bug, authentication
relations: TenantAccountController,owning_project
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T09:47:39.3774437Z
lastAccessedAt: 2026-05-14T09:47:39.3774439Z
---

TenantAccountController 的登录流程中缺失了设置 owning_project 声明（Claim）的逻辑，导致多租户登录用户无法正确关联项目。