---
name: AiChatApp,CliExecutorService.cs,性能优化,资源浪费
description: AiChatApp 项目的 CliExecutorService.cs 在 CliPersistenceEnabled 关闭时默认采用 ExecuteSingl...
type: user
userId: 1
tags: AiChatApp,CliExecutorService.cs,性能优化,资源浪费
relations: AiChatApp,CliExecutorService.cs,CliPersistenceEnabled
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-18T10:22:55.8818193Z
lastAccessedAt: 2026-05-18T10:22:55.8818194Z
---

AiChatApp 项目的 CliExecutorService.cs 在 CliPersistenceEnabled 关闭时默认采用 ExecuteSingleShotAsync 模式，导致频繁创建和销毁操作系统进程，造成 CPU、内存资源浪费及响应延迟。