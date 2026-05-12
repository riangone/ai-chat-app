---
name: bug-fix, dotnet, dependency-injection, project-scope
description: AiChatApp 修复了一个严重的 DI 作用域 Bug：当 `UserAuthService` 和 `AuditLogService` 通过 `IServi...
type: user
userId: 1
tags: bug-fix, dotnet, dependency-injection, project-scope
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T02:01:04.1484659Z
lastAccessedAt: 2026-05-12T02:01:04.1484659Z
---

AiChatApp 修复了一个严重的 DI 作用域 Bug：当 `UserAuthService` 和 `AuditLogService` 通过 `IServiceScopeFactory` 创建新作用域时，由于 `ProjectScope` 是 Scoped 生命周期且未经过中间件初始化，导致 `IsSet` 为 false 并抛出异常。