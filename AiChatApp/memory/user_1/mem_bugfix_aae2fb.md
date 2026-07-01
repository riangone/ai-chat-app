---
name: bugfix,roles,DELETE,ProjectApiController
description: ProjectApiController.cs:83 中的角色 DELETE bug 已被修复，原因为缺少 .DisableAntiforgery() 导致删除...
type: user
userId: 1
tags: bugfix,roles,DELETE,ProjectApiController
relations: ProjectApiController,roles
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-30T09:13:52.9977617Z
lastAccessedAt: 2026-06-30T09:13:52.9977619Z
boundAgentRole: 
---

ProjectApiController.cs:83 中的角色 DELETE bug 已被修复，原因为缺少 .DisableAntiforgery() 导致删除时返回 400 错误。