---
name: htmx,javascript,pattern,notes
description: HTMXイベントハンドラはインラインに複雑なロジックを書くとスコープ問題が発生するため、グローバルなJavaScript関数（`selectNote(el)`,...
type: user
userId: 1
tags: htmx,javascript,pattern,notes
relevanceScore: 80
accessCount: 2
createdAt: 2026-04-27T15:34:53.3383392Z
lastAccessedAt: 2026-04-27T15:44:58.6817584Z
---

HTMXイベントハンドラはインラインに複雑なロジックを書くとスコープ問題が発生するため、グローバルなJavaScript関数（`selectNote(el)`, `onNoteCreated(event)`）を定義してから呼び出すパターンを採用した。