---
name: hardcode-policy
description: ハードコードされた接続文字列・ファイルパス・設定値はすべて `appsettings.json` に移動する。コード内に直接値を書くことは禁止。

**Why:...
type: user
userId: 0
tags: hardcode-policy
relevanceScore: 80
accessCount: 2
createdAt: 2026-04-27T22:17:26.0791354Z
lastAccessedAt: 2026-04-27T22:52:30.8592424Z
---

ハードコードされた接続文字列・ファイルパス・設定値はすべて `appsettings.json` に移動する。コード内に直接値を書くことは禁止。

**Why:** ユーザーが「彻底解决硬编码（ハードコードを徹底的に解決せよ）」と明示的に指示した。

**How to apply:** 新機能追加・バグ修正を問わず、文字列リテラルとして埋め込まれた設定値（URL、パス、タイムアウト値、モデル名など）を発見したら即座に `appsettings.json` + `IConfiguration` 経由に移行する。