---
name: refactoring,stability,C#
description: 在运行时编译的处理器代码中，将 `dynamic` 类型替换为 `IDictionary<string, object>`，以避免对 `Microsoft.CS...
type: user
userId: 1
tags: refactoring,stability,C#
relations: ProjectHookLoader
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-28T10:08:15.9169092Z
lastAccessedAt: 2026-05-28T10:08:15.9169095Z
---

在运行时编译的处理器代码中，将 `dynamic` 类型替换为 `IDictionary<string, object>`，以避免对 `Microsoft.CSharp` 动态绑定库的依赖并提升稳定性。