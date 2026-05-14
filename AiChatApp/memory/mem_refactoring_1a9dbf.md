---
name: refactoring,code_quality
description: UserAuthService 提取了私有方法 InsertUserAsync，消除了 CreateAsync、RegisterAsync 和 Register...
type: user
userId: 1
tags: refactoring,code_quality
relations: UserAuthService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:41:27.7488850Z
lastAccessedAt: 2026-05-12T23:41:27.7488851Z
---

UserAuthService 提取了私有方法 InsertUserAsync，消除了 CreateAsync、RegisterAsync 和 RegisterCustomerAsync 中约 90 行重复的 SQL 插入逻辑。