---
name: aisservice,codex,copilot,error-handling,normalization
description: AiService.cs に NormalizeProviderError 関数が追加され、codex 認証失敗（ChatGPT アカウントで API モデル利...
type: user
userId: 1
tags: aisservice,codex,copilot,error-handling,normalization
relevanceScore: 80
accessCount: 2
createdAt: 2026-04-27T17:02:22.6678336Z
lastAccessedAt: 2026-04-28T00:54:06.8981922Z
---

AiService.cs に NormalizeProviderError 関数が追加され、codex 認証失敗（ChatGPT アカウントで API モデル利用不可）と copilot の 402/quota 超過エラーを、生の CLI ログではなく案内付きの短いメッセージに正規化するようになった