---
name: netyamlforge,pathbase,caddy,fix,routing
description: NetYamlForge 项目路径基址问题已修复：在 Program.cs 中添加了 app.UsePathBase("/nyf")，并将 Caddy 配置从 ...
type: user
userId: 1
tags: netyamlforge,pathbase,caddy,fix,routing
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-30T10:57:06.4174558Z
lastAccessedAt: 2026-04-30T10:57:06.4174559Z
---

NetYamlForge 项目路径基址问题已修复：在 Program.cs 中添加了 app.UsePathBase("/nyf")，并将 Caddy 配置从 handle_path /nyf* 改为 handle /nyf*，以确保 /nyf 前缀完整传递给后端