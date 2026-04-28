---
name: notes,swiper,frontend,htmx
description: notesSwiper は `window.notesSwiper = new Swiper(...)` としてグローバルに登録し、どのスコープからも参照できる...
type: user
userId: 1
tags: notes,swiper,frontend,htmx
relevanceScore: 80
accessCount: 2
createdAt: 2026-04-27T15:50:03.6639566Z
lastAccessedAt: 2026-04-27T16:18:01.2870578Z
---

notesSwiper は `window.notesSwiper = new Swiper(...)` としてグローバルに登録し、どのスコープからも参照できるようにする。HTMXのDOM更新後にスライドするためslideTo()はsetTimeoutで遅延させる。