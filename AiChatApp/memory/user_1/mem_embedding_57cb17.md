---
name: embedding,database,troubleshooting
description: 向量索引为空的根本原因是 photo_embeddings 表无数据，需要通过每小时运行一次的 embedding_generator 任务或在 UI 处理队列...
type: user
userId: 1
tags: embedding,database,troubleshooting
relations: photo_embeddings,embedding_generator,annotation_status
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-22T23:18:47.4532480Z
lastAccessedAt: 2026-06-22T23:18:47.4532484Z
boundAgentRole: 
---

向量索引为空的根本原因是 photo_embeddings 表无数据，需要通过每小时运行一次的 embedding_generator 任务或在 UI 处理队列页面手动触发“向量嵌入生成”批处理，为 annotation_status='done' 的照片生成 384 维向量。