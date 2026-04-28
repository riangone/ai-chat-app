---
name: htmx,syntax,version
description: プロジェクトはHTMX 2.0を使用しており、イベント属性の構文：
- DOM標準イベント（click等）: `hx-on:click`（コロン1つ）
- HT...
type: user
userId: 1
tags: htmx,syntax,version
relevanceScore: 80
accessCount: 1
createdAt: 2026-04-27T15:34:53.3400366Z
lastAccessedAt: 2026-04-27T16:30:19.5225047Z
---

プロジェクトはHTMX 2.0を使用しており、イベント属性の構文：
- DOM標準イベント（click等）: `hx-on:click`（コロン1つ）
- HTMXイベント（htmx:afterRequest等）: `hx-on::after-request`（コロン2つ、コロン2つ目が `htmx:` プレフィックスを省略する）

サーバーサイドレンダリング（C# の NotesEndpoints.cs 等）でノートアイテムのクリックに `hx-on::click` を使うと発火しない。`hx-on:click` に変更して修正した実績あり。