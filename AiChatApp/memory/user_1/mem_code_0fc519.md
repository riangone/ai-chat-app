---
name: code,worker,routing,PhotoAnnotationHandlers
description: PhotoAnnotationHandlers.cs 新增了 provider 与 jobId 的映射关系以动态路由触发对应的 Worker（例如 lmstud...
type: user
userId: 1
tags: code,worker,routing,PhotoAnnotationHandlers
relations: PhotoAnnotationHandlers.cs,worker_routing,lmstudio_annotation_worker,antigravity_cli_worker
relevanceScore: 24
accessCount: 0
createdAt: 2026-06-21T23:28:09.0491544Z
lastAccessedAt: 2026-06-21T23:28:09.0491546Z
boundAgentRole: 
---

PhotoAnnotationHandlers.cs 新增了 provider 与 jobId 的映射关系以动态路由触发对应的 Worker（例如 lmstudio_annotation_worker），不再硬编码为 antigravity_cli_worker。