---
name: netYamlForge,caddy,pathBase,bug,aspnetcore,routing
description: 访问 ai.0101.click/nyf 时重定向到 ai.0101.click/Account/Login?ReturnUrl=%2F，丢失了 /nyf 前缀...
type: user
userId: 1
tags: netYamlForge,caddy,pathBase,bug,aspnetcore,routing
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 11
createdAt: 2026-04-30T10:44:06.0662579Z
lastAccessedAt: 2026-05-14T06:28:12.6046994Z
---

访问 ai.0101.click/nyf 时重定向到 ai.0101.click/Account/Login?ReturnUrl=%2F，丢失了 /nyf 前缀。根因是 Caddy 使用 handle_path 剥离 /nyf 前缀后转发给后端，导致 ASP.NET Core 应用不知道自己被挂载在 /nyf 子路径，修复方案是在 NetYamlForge 的 Program.cs 中添加 app.UsePathBase("/nyf") 并调整 Caddy 配置传递完整路径