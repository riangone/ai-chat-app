---
name: refactor,javascript,initialization
description: 整合了 index.html 中重複的 DOMContentLoaded 監聽器，確保全局變量 window.swiper 與局部變量 swiper 同步賦值，...
type: user
userId: 1
tags: refactor,javascript,initialization
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-28T07:07:38.7406395Z
lastAccessedAt: 2026-04-28T07:07:38.7406396Z
---

整合了 index.html 中重複的 DOMContentLoaded 監聽器，確保全局變量 window.swiper 與局部變量 swiper 同步賦值，避免作用域問題導致的切換失敗。