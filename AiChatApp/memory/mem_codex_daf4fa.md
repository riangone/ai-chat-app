---
name: codex,bug,system-prompt-leak,AiService,ExecuteCliAsync
description: codexプロバイダーが認証エラーやモデル非互換エラーで失敗した際、AiService.csのExecuteCliAsyncがエラー出力からシステムプロンプトを...
type: user
userId: 1
tags: codex,bug,system-prompt-leak,AiService,ExecuteCliAsync
relevanceScore: 80
accessCount: 4
createdAt: 2026-04-27T22:10:06.5333659Z
lastAccessedAt: 2026-04-28T12:28:15.6828961Z
---

codexプロバイダーが認証エラーやモデル非互換エラーで失敗した際、AiService.csのExecuteCliAsyncがエラー出力からシステムプロンプトをユーザーに露出させるバグが存在する。原因はShouldIgnoreStderrLineのフィルタリングが不十分なこと。