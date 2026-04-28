---
name: htmx,syntax,notes,bug-fix,frontend
description: HTMXのDOM標準イベント（click等）はサーバーサイドレンダリングHTMLで `hx-on:click`（シングルコロン）を使う。`hx-on::clic...
type: user
userId: 1
tags: htmx,syntax,notes,bug-fix,frontend
relevanceScore: 80
accessCount: 1
createdAt: 2026-04-27T15:50:03.6619291Z
lastAccessedAt: 2026-04-27T16:30:19.5223368Z
---

HTMXのDOM標準イベント（click等）はサーバーサイドレンダリングHTMLで `hx-on:click`（シングルコロン）を使う。`hx-on::click`（ダブルコロン）を使うとイベントが発火しない。NotesEndpoints.csでの修正実績あり。