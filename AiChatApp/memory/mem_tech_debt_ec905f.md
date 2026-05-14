---
name: tech_debt,refactoring,UserAuthService
description: UserAuthService 中的用户插入 SQL 逻辑存在约 100 行的重复代码，涉及 CreateAsync、RegisterAsync 和 Regis...
type: user
userId: 1
tags: tech_debt,refactoring,UserAuthService
relations: UserAuthService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T23:30:35.7709775Z
lastAccessedAt: 2026-05-12T23:30:35.7709777Z
---

UserAuthService 中的用户插入 SQL 逻辑存在约 100 行的重复代码，涉及 CreateAsync、RegisterAsync 和 RegisterCustomerAsync 三个方法。