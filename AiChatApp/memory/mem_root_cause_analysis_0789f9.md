---
name: root-cause-analysis, dotnet, service-scope
description: 确定了 Bug 的根源在于参数化和非参数化 `GetConnectionAsync()` 的调用逻辑冲突，新创建的作用域无法继承当前请求的 `ProjectSc...
type: user
userId: 1
tags: root-cause-analysis, dotnet, service-scope
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T02:01:04.1864633Z
lastAccessedAt: 2026-05-12T02:01:04.1864633Z
---

确定了 Bug 的根源在于参数化和非参数化 `GetConnectionAsync()` 的调用逻辑冲突，新创建的作用域无法继承当前请求的 `ProjectScope` 状态。