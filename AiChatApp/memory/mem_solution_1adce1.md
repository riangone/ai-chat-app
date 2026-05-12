---
name: solution, dotnet, architecture
description: 采取的解决方案是让 `UserAuthService` 等服务直接通过当前作用域的 `_scope.Current.Name` 获取项目信息，避免创建不包含必要...
type: user
userId: 1
tags: solution, dotnet, architecture
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T02:01:04.2194433Z
lastAccessedAt: 2026-05-12T02:01:04.2194434Z
---

采取的解决方案是让 `UserAuthService` 等服务直接通过当前作用域的 `_scope.Current.Name` 获取项目信息，避免创建不包含必要上下文的新 DI 作用域。