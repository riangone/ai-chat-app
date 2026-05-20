---
name: Gemini CLI,故障排除,Plan Mode,yolo
description: Gemini CLI 在处理具有“操作意图”的消息时可能会进入交互式的 Plan Mode，即使使用了 --yolo 标志，这会导致在 I/O 重定向管道中进程...
type: user
userId: 1
tags: Gemini CLI,故障排除,Plan Mode,yolo
relations: Gemini CLI,CliExecutorService,Plan Mode
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-20T02:22:25.2096698Z
lastAccessedAt: 2026-05-20T02:22:25.2096699Z
---

Gemini CLI 在处理具有“操作意图”的消息时可能会进入交互式的 Plan Mode，即使使用了 --yolo 标志，这会导致在 I/O 重定向管道中进程挂起。