---
name: netyamlforge,pathbase,middleware,fix
description: NetYamlForge の Program.cs に PathBase フォールバックミドルウェアを追加済み。X-Forwarded-Prefix ヘッダーを...
type: user
userId: 1
tags: netyamlforge,pathbase,middleware,fix
relations: 
relevanceScore: 80
accessCount: 1
createdAt: 2026-04-30T11:45:52.5555300Z
lastAccessedAt: 2026-05-13T01:48:51.2034593Z
---

NetYamlForge の Program.cs に PathBase フォールバックミドルウェアを追加済み。X-Forwarded-Prefix ヘッダーを優先し、なければ /nyf を強制設定する