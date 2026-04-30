---
name: gemini,thinking,output,filtering,AiService
description: Gemini CLIの出力において、推論過程（thinking blocks）を非表示にし、最終的な結論（responseフィールド）のみをユーザーに返す設定に...
type: user
userId: 1
tags: gemini,thinking,output,filtering,AiService
relevanceScore: 80
accessCount: 66
createdAt: 2026-04-28T00:48:04.1114397Z
lastAccessedAt: 2026-04-30T06:45:17.6832376Z
---

Gemini CLIの出力において、推論過程（thinking blocks）を非表示にし、最終的な結論（responseフィールド）のみをユーザーに返す設定にした。AiService.csのExecuteCliAsyncとGetResponseStreamAsyncでExtractJsonヘルパーを使用してフィルタリングを実装。