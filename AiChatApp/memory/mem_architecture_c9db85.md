---
name: architecture,endpoints,cooperate-mode
description: 当前 ChatEndpoints.cs 中的 /chat/cooperate/stream 路由使用的是 CooperateAsync 方法配合回调，而非 Co...
type: user
userId: 1
tags: architecture,endpoints,cooperate-mode
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-28T23:19:08.6281868Z
lastAccessedAt: 2026-04-28T23:19:08.6281869Z
---

当前 ChatEndpoints.cs 中的 /chat/cooperate/stream 路由使用的是 CooperateAsync 方法配合回调，而非 CooperateStreamAsync。