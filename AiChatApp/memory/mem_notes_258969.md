---
name: notes,htmx,swiper,transition,frontend
description: ノート一覧アイテムの選択時は `hx-on:click="selectNote(this)"` でSwiper遷移を発火（シングルコロン）。新規作成ボタンはサー...
type: user
userId: 1
tags: notes,htmx,swiper,transition,frontend
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 80
createdAt: 2026-04-27T15:34:53.3418648Z
lastAccessedAt: 2026-05-14T01:39:02.8402851Z
---

ノート一覧アイテムの選択時は `hx-on:click="selectNote(this)"` でSwiper遷移を発火（シングルコロン）。新規作成ボタンはサーバー応答後に `hx-on::after-request="onNoteCreated(event)"` で遷移（HTMXイベントなのでダブルコロン）。関連ファイル: `wwwroot/index.html`, `Endpoints/NotesEndpoints.cs`。

`notesSwiper` は `window.notesSwiper` としてグローバルに登録し、どのスコープからでも参照できるようにする。HTMXのコンテンツ更新後にスライドするため `setTimeout` で遅延させる。