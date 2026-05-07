---
name: netyamlforge,pathbase,middleware,fix
description: NetYamlForge の Program.cs に PathBase フォールバックミドルウェアを追加済み。X-Forwarded-Prefix ヘッダーを...
type: user
userId: 1
tags: netyamlforge,pathbase,middleware,fix
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-30T11:45:52.5555300Z
lastAccessedAt: 2026-04-30T11:45:52.5555301Z
---

NetYamlForge の Program.cs に PathBase フォールバックミドルウェアを追加済み。X-Forwarded-Prefix ヘッダーを優先し、なければ /nyf を強制設定する