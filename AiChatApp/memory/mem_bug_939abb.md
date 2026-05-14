---
name: bug, security
description: 发现 ProjectScopeMiddleware 虽然已定义，但在 Program.cs 中未被注册，导致缺乏跨项目的访问控制。
type: user
userId: 1
tags: bug, security
relations: ProjectScopeMiddleware,Program.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T09:47:39.3310455Z
lastAccessedAt: 2026-05-14T09:47:39.3310455Z
---

发现 ProjectScopeMiddleware 虽然已定义，但在 Program.cs 中未被注册，导致缺乏跨项目的访问控制。