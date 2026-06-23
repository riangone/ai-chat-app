---
name: 照片标注,lmstudio,jobs.yml,系统配置,故障排查
description: 照片无法标注的根本原因在于照片以 `provider=lmstudio` 入队，但 `jobs.yml` 中对应的 `lmstudio_annotation_w...
type: user
userId: 1
tags: 照片标注,lmstudio,jobs.yml,系统配置,故障排查
relations: lmstudio_annotation_worker,jobs.yml,processing_queue
relevanceScore: 63
accessCount: 0
createdAt: 2026-06-21T12:46:11.7744374Z
lastAccessedAt: 2026-06-21T12:46:11.7744375Z
boundAgentRole: 
---

照片无法标注的根本原因在于照片以 `provider=lmstudio` 入队，但 `jobs.yml` 中对应的 `lmstudio_annotation_worker` 处于禁用状态（`enabled: false`），导致任务永远不会被处理。