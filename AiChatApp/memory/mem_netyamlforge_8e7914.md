---
name: netyamlforge,caddy,pathbase,aspnetcore,static-assets,bug
description: NetYamlForge 静态资源路径（JS/CSS）在应用修复后仍缺少 /nyf 前缀，根本原因是 Caddy 使用 handle_path 剥离了 /nyf...
type: user
userId: 1
tags: netyamlforge,caddy,pathbase,aspnetcore,static-assets,bug
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 4
createdAt: 2026-04-30T11:33:00.6072473Z
lastAccessedAt: 2026-05-14T06:28:12.6046583Z
---

NetYamlForge 静态资源路径（JS/CSS）在应用修复后仍缺少 /nyf 前缀，根本原因是 Caddy 使用 handle_path 剥离了 /nyf 前缀，导致 ASP.NET Core 的 PathBase 为空，~/  和 @Url.Content 均无法生成含前缀的路径