---
name: NetYamlForge PathBase 路径问题与修复
description: 访问 `ai.0101.click/nyf` 时被重定向到 `ai.0101.click/Account/Login?ReturnUrl=%2F`，缺少 `/n...
type: user
userId: 0
tags: NetYamlForge PathBase 路径问题与修复
relations: relevanceScore: 80
relevanceScore: 62
accessCount: 2
createdAt: 2026-04-30T10:43:45.3027202Z
lastAccessedAt: 2026-06-21T10:40:47.8180973Z
boundAgentRole: 
---

访问 `ai.0101.click/nyf` 时被重定向到 `ai.0101.click/Account/Login?ReturnUrl=%2F`，缺少 `/nyf` 前缀。

**Why:** Caddy 使用 `handle_path` 剥离了 `/nyf` 前缀后转发给后端，ASP.NET Core 应用不知道自己被挂载在 `/nyf` 子路径下，生成的重定向 URL 因此不含该前缀。

**How to apply:** 修复需要两步：
1. 在 NetYamlForge 的 `Program.cs` 中添加 `app.UsePathBase("/nyf");`（放在其他中间件之前）
2. 调整 Caddy 配置，改用 `handle` 而非 `handle_path`，或通过 `X-Forwarded-Prefix` 头传递前缀信息，确保完整路径传递给后端