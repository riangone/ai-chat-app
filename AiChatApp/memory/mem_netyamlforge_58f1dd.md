---
name: netyamlforge,bug,pathbase,aspnetcore
description: NetYamlForge 的 Program.cs 曾有 PathBase 双重叠加 bug：自定义中间件设置 PathBase=/nyf 后，UsePathB...
type: user
userId: 1
tags: netyamlforge,bug,pathbase,aspnetcore
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 2
createdAt: 2026-04-30T11:44:48.9783479Z
lastAccessedAt: 2026-05-14T06:28:12.6047903Z
---

NetYamlForge 的 Program.cs 曾有 PathBase 双重叠加 bug：自定义中间件设置 PathBase=/nyf 后，UsePathBase('/nyf') 再叠加，导致 /nyf/nyf；修复方案是只保留 app.UsePathBase('/nyf')