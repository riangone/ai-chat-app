---
name: bug-fix,authentication,security
description: 用户在登录时遇到“ID或密码错误”提示，经排查发现是由于 Seeder 文件中密码哈希算法不一致（部分使用 SHA256 而非项目标准的 BCrypt）导致的。
type: user
userId: 1
tags: bug-fix,authentication,security
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T03:21:40.0966991Z
lastAccessedAt: 2026-05-12T03:21:40.0966991Z
---

用户在登录时遇到“ID或密码错误”提示，经排查发现是由于 Seeder 文件中密码哈希算法不一致（部分使用 SHA256 而非项目标准的 BCrypt）导致的。