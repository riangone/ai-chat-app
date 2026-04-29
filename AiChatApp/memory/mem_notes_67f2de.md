---
name: notes,htmx,swiper,transition,onclick,frontend
description: ノート一覧アイテムのクリックイベントは `hx-on::click` や `hx-on:click` ではなく、標準の `onclick="selectNote...
type: user
userId: 1
tags: notes,htmx,swiper,transition,onclick,frontend
relevanceScore: 80
accessCount: 9
createdAt: 2026-04-27T16:01:33.0454543Z
lastAccessedAt: 2026-04-29T08:00:18.1619161Z
---

ノート一覧アイテムのクリックイベントは `hx-on::click` や `hx-on:click` ではなく、標準の `onclick="selectNote(this)"` を使うべき。HTMXリクエストと同時に `slideTo()` を呼ぶ場合、`hx-on` 系属性はSwiper遷移が発火しないバグが発生した（2026-04-27確認）。関連ファイル: `Endpoints/NotesEndpoints.cs`