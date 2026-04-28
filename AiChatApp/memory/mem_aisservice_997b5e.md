---
name: aisservice,timeout,process-management
description: AIプロセスのタイムアウト設定。appsettings.json:600秒、AiService.csデフォルト:300秒。正常終了時にタイムアウトタスクをキャンセルする改善済み。
type: project
userId: 1
tags: aisservice,timeout,process-management
relevanceScore: 80
accessCount: 7
createdAt: 2026-04-27T17:43:00.9369483Z
lastAccessedAt: 2026-04-27T23:50:54.2834331Z
---

AIプロセスのタイムアウト設定（2026-04-27改善済み）：
- `appsettings.json` の `TimeoutSeconds`: 600秒（10分）
- `AiService.cs` コード内デフォルト値: 300秒（5分）
- `GetResponseStreamAsync` と `ExecuteCliAsync` の両方で `CancellationTokenSource` を使用し、プロセスが正常終了した場合にタイムアウト監視タスクをキャンセルするよう改善

**Why:** ユーザーがAIの回答が途中で中断される問題を報告。以前の90秒のハードタイムアウトが原因で、長時間の応答が遮断されていた。

**How to apply:** タイムアウト関連の変更を行う場合は両方の値を一貫して更新する。