---
name: logic, sync
description: JpcsUserSyncService 将 JPCS 员工同步到 system.db 时，硬编码 owning_project 为 'jpcs'，可能与 'jp...
type: user
userId: 1
tags: logic, sync
relations: JpcsUserSyncService,jpcs,jpiere-cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T09:47:39.4714844Z
lastAccessedAt: 2026-05-14T09:47:39.4714845Z
---

JpcsUserSyncService 将 JPCS 员工同步到 system.db 时，硬编码 owning_project 为 'jpcs'，可能与 'jpiere-cs' 项目标识符存在命名不一致风险。