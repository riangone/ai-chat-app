---
name: codex,bug,system-prompt-leak,AiService,ExecuteCliAsync
description: codexプロバイダーが認証エラーやモデル非互換エラーで失敗した際、AiService.csのExecuteCliAsyncがエラー出力からシステムプロンプトを...
type: user
userId: 1
tags: codex,bug,system-prompt-leak,AiService,ExecuteCliAsync
relevanceScore: 80
accessCount: 3
createdAt: 2026-04-27T22:10:06.5333659Z
lastAccessedAt: 2026-04-27T22:34:21.1875706Z
---

codexプロバイダーが認証エラーやモデル非互換エラーで失敗した際、AiService.csのExecuteCliAsyncがエラー出力からシステムプロンプトをユーザーに露出させるバグが存在する。原因はShouldIgnoreStderrLineのフィルタリングが不十分なこと。