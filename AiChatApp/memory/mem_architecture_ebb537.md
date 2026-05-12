---
name: architecture,streaming,sse
description: 实时对话采用 SSE (Server-Sent Events) 流式传输，前端使用 JavaScript 的 fetch API 请求 /api/chat/st...
type: user
userId: 1
tags: architecture,streaming,sse
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-09T05:15:09.3378224Z
lastAccessedAt: 2026-05-09T05:15:09.3378225Z
---

实时对话采用 SSE (Server-Sent Events) 流式传输，前端使用 JavaScript 的 fetch API 请求 /api/chat/stream 并通过 Reader 实时读取并显示数据。