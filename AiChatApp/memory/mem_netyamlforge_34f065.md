---
name: netyamlforge,aspnetcore,pathbase,middleware,caddy
description: ASP.NET Core の UsePathBase("/nyf") は Caddy が /nyf を剥ぎ取った後では機能しない。X-Forwarded-Pre...
type: user
userId: 1
tags: netyamlforge,aspnetcore,pathbase,middleware,caddy
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 2
createdAt: 2026-04-30T11:45:52.5379477Z
lastAccessedAt: 2026-05-15T05:00:34.6718358Z
---

ASP.NET Core の UsePathBase("/nyf") は Caddy が /nyf を剥ぎ取った後では機能しない。X-Forwarded-Prefix を読み取るか強制的に PathBase を /nyf に設定するフォールバックミドルウェアが必要