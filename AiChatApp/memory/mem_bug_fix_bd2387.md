---
name: bug-fix,api,nginx
description: 修复了 Streaming Error: 405 错误，将后端 /api/chat/stream 接口从 GET 改为 POST，并修正了 Nginx 对 /g...
type: user
userId: 1
tags: bug-fix,api,nginx
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-07T10:24:41.4083046Z
lastAccessedAt: 2026-05-07T10:24:41.4083047Z
---

修复了 Streaming Error: 405 错误，将后端 /api/chat/stream 接口从 GET 改为 POST，并修正了 Nginx 对 /gennai/ 路径前缀的转发配置。