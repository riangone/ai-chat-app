---
name: bugfix,backend,nginx,api
description: 已修复导致 “Streaming Error: 405” 的问题，原因是后端接口方法不匹配（前端发送 POST 而后端仅支持 GET）以及 Nginx 路径前缀...
type: user
userId: 1
tags: bugfix,backend,nginx,api
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-07T10:22:58.6246953Z
lastAccessedAt: 2026-05-07T10:22:58.6246953Z
---

已修复导致 “Streaming Error: 405” 的问题，原因是后端接口方法不匹配（前端发送 POST 而后端仅支持 GET）以及 Nginx 路径前缀配置不当。