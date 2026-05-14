---
name: bug-fix,database-migration
description: 修复了因 ALTER TABLE 导致已有用户 owning_project 字段为 NULL 的问题，已将 jpcs_admin 和 jpcs_user1 正...
type: user
userId: 1
tags: bug-fix,database-migration
relations: jpcs,jpcs_admin,jpcs_user1
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T09:38:36.8654093Z
lastAccessedAt: 2026-05-14T09:38:36.8654093Z
---

修复了因 ALTER TABLE 导致已有用户 owning_project 字段为 NULL 的问题，已将 jpcs_admin 和 jpcs_user1 正确归属于 jpcs 项目。