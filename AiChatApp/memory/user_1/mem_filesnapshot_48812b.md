---
name: FileSnapshot,ShadowGitService,SnapshotEndpoints,architecture,git,snapshot
description: 新增了 FileSnapshot 模型（用于文件快照）、ShadowGitService（在 .shadow-git/ 目录下追踪和撤销 Agent 更改）以及...
type: user
userId: 1
tags: FileSnapshot,ShadowGitService,SnapshotEndpoints,architecture,git,snapshot
relations: FileSnapshot,ShadowGitService,SnapshotEndpoints
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-23T02:46:03.4082215Z
lastAccessedAt: 2026-06-23T02:46:03.4082215Z
boundAgentRole: 
---

新增了 FileSnapshot 模型（用于文件快照）、ShadowGitService（在 .shadow-git/ 目录下追踪和撤销 Agent 更改）以及 SnapshotEndpoints API（支持快照的 CRUD、diff 和 revert 功能）。