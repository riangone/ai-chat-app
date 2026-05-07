---
name: notes,swiper,bug,ui,css,decision
description: ノートのスライドナビゲーションでSwiperの `nested: true` と縦方向初期化が壊れる問題が発生。親Swiperがスライドを非表示（高さゼロ）にし...
type: user
userId: 1
tags: notes,swiper,bug,ui,css,decision
relevanceScore: 80
accessCount: 4
createdAt: 2026-04-27T16:09:12.1907641Z
lastAccessedAt: 2026-04-30T11:44:09.6529522Z
---

ノートのスライドナビゲーションでSwiperの `nested: true` と縦方向初期化が壊れる問題が発生。親Swiperがスライドを非表示（高さゼロ）にしている間に初期化されるためスライド計算が崩れる。解決策としてSwiperを完全に排除し、CSSの `translateY` transition に置き換えた。