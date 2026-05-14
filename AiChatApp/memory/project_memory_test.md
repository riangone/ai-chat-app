---
name: 記憶機能テスト記録
description: ユーザーはAIチャットアプリの記憶機能（`MemoryConsolidationService`）をテストした。

**確認済み動作:**
- `POST /a...
type: user
userId: 0
tags: 記憶機能テスト記録
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 78
createdAt: 2026-04-26T23:54:47.4544884Z
lastAccessedAt: 2026-05-14T03:49:17.9286967Z
---

ユーザーはAIチャットアプリの記憶機能（`MemoryConsolidationService`）をテストした。

**確認済み動作:**
- `POST /api/memories` — フォーム形式で記憶追加 ✅
- `GET /api/memories` — HTMLフラグメントで一覧返却 ✅
- `DELETE /api/memories/:id` — DB削除 ✅
- 自動統合はチャット後にfire-and-forgetで `gemini`/`claude` CLI経由で実行

**How to apply:** 記憶機能のデバッグ時は `MemoryConsolidationService.cs` の `Console.WriteLine("[Memory]...)` ログを確認する。

**Claude Code側の対応:**
- `autoMemoryEnabled: true` と `autoDreamEnabled: true` を `~/.claude/settings.json` に追加済み（2026-04-27）