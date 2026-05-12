---
name: notes-swiper-htmx-config
description: NotesパネルのSwiperをHTMXと組み合わせる際の必須設定：

**Swiper初期化オプション**:
- `observer: true` と `ob...
type: user
userId: 0
tags: notes-swiper-htmx-config
relevanceScore: 80
accessCount: 11
createdAt: 2026-04-27T15:49:49.1450963Z
lastAccessedAt: 2026-05-11T08:08:47.7458952Z
---

NotesパネルのSwiperをHTMXと組み合わせる際の必須設定：

**Swiper初期化オプション**:
- `observer: true` と `observeParents: true` を必ず追加。HTMXでコンテンツが動的に書き換わった後も自動でサイズ再計算される。
- インスタンスは `window.notesSwiper = new Swiper(...)` としてグローバルに登録する。

**全画面レイアウトCSS**:
- `#notes-panel` と `#notes-vertical-swiper` に `flex-1 min-h-0` を付与して親の高さを引き継ぐ。
- `.swiper-wrapper`, `.swiper-slide` には `height: 100% !important` をCSSで強制適用。
- これにより編集画面が下半分しか表示されない問題を防ぐ。

**HTMX連携タイミング**:
- `selectNote()` / `onNoteCreated()` 内で `window.notesSwiper.slideTo(1)` を呼ぶ際は `setTimeout` で遅延させてHTMXのDOM更新を待つ。

**Why:** 上記設定なしだと、スライド遷移が発火しない・エディタが全画面にならないバグが発生した（2026-04-27修正）。
**How to apply:** NotesパネルのSwiper設定変更・CSSレイアウト調整時に参照する。