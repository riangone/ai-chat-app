---
name: notes,swiper,implementation,pattern,ui
description: ノートスライドの実装パターン：`#notes-slide-list`（translateY(0)）と `#notes-slide-editor`（初期trans...
type: user
userId: 1
tags: notes,swiper,implementation,pattern,ui
relevanceScore: 80
accessCount: 1
createdAt: 2026-04-27T16:09:12.1942086Z
lastAccessedAt: 2026-04-27T16:18:01.2847614Z
---

ノートスライドの実装パターン：`#notes-slide-list`（translateY(0)）と `#notes-slide-editor`（初期translateY(100%)）の2つの `absolute inset-0` divを使用。`window.goToNoteSlide(index)` でスライド切り替えを行う。`notesSwiper.slideTo()` は使用しない。