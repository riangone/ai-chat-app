---
name: refactoring,security,dotnet
description: 修复了 SystemDbTestUserSeeder 和 DefaultAdminSeeder 中的哈希算法问题，确保所有初始用户均使用 BCrypt 进行密码...
type: user
userId: 1
tags: refactoring,security,dotnet
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T03:21:40.1292070Z
lastAccessedAt: 2026-05-12T03:21:40.1292071Z
---

修复了 SystemDbTestUserSeeder 和 DefaultAdminSeeder 中的哈希算法问题，确保所有初始用户均使用 BCrypt 进行密码加密，并清理了相关的代码引用。