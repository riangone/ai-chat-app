---
name: AiChatApp,MemoryFileService.SearchAsync,性能优化,搜索瓶颈
description: AiChatApp 项目的 MemoryFileService.SearchAsync 存在线性记忆搜索瓶颈，处理每个用户提词时会遍历所有已加载的记忆文件进行关...
type: user
userId: 1
tags: AiChatApp,MemoryFileService.SearchAsync,性能优化,搜索瓶颈
relations: AiChatApp,MemoryFileService.SearchAsync
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-18T10:22:55.9649756Z
lastAccessedAt: 2026-05-18T10:22:55.9649758Z
---

AiChatApp 项目的 MemoryFileService.SearchAsync 存在线性记忆搜索瓶颈，处理每个用户提词时会遍历所有已加载的记忆文件进行关键词打分计算，影响效率。