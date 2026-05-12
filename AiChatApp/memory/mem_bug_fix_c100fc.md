---
name: bug_fix,authentication,admin
description: 用户 admin 的登录问题已修复。之前由于 SystemDbTestUserSeeder.cs 硬编码了错误的密码哈希（Test @memory/mem_pr...
type: user
userId: 1
tags: bug_fix,authentication,admin
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T03:34:48.5297884Z
lastAccessedAt: 2026-05-12T03:34:48.5297884Z
---

用户 admin 的登录问题已修复。之前由于 SystemDbTestUserSeeder.cs 硬编码了错误的密码哈希（Test @memory/mem_preference_ef1234.md!）且格式不符合 ASP.NET Core PasswordHasher 标准，导致验证失败。