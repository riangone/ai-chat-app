---
name: bugfix,PhotoAnnotationHandlers.cs,AnnotateNowHandler,processing_queue
description: 在 PhotoAnnotationHandlers.cs 中修复了立即标注处理器 (AnnotateNowHandler) 的逻辑：当用户点击立即标注时，会强制...
type: user
userId: 1
tags: bugfix,PhotoAnnotationHandlers.cs,AnnotateNowHandler,processing_queue
relations: PhotoAnnotationHandlers.cs,AnnotateNowHandler,processing_queue
relevanceScore: 63
accessCount: 0
createdAt: 2026-06-21T12:15:51.2375602Z
lastAccessedAt: 2026-06-21T12:15:51.2375604Z
boundAgentRole: 
---

在 PhotoAnnotationHandlers.cs 中修复了立即标注处理器 (AnnotateNowHandler) 的逻辑：当用户点击立即标注时，会强制删除 processing_queue 中该照片所有状态为 'queued' 或 'processing' 的旧记录，并以正确的 provider 和 priority=10 重新入队。