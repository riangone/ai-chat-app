---
name: agent-system-prompt-leak
description: 記憶クエリ（「記得我的名字吗？」等）に応答する際、ClaudeCodeとGeminiは記憶から情報を取得できるが、返信の冒頭にシステムプロンプトの内容（例：「S...
type: user
userId: 0
tags: agent-system-prompt-leak
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 168
createdAt: 2026-04-28T02:06:24.7274365Z
lastAccessedAt: 2026-05-17T01:56:46.2531976Z
---

記憶クエリ（「記得我的名字吗？」等）に応答する際、ClaudeCodeとGeminiは記憶から情報を取得できるが、返信の冒頭にシステムプロンプトの内容（例：「System: あなたは高度なAIアシスタントです」）が含まれてしまうバグが存在する。他のエージェントは記憶へのアクセス自体ができない。

**Why:** システムプロンプトがエージェントへの入力として渡されているが、エージェントがそれをそのまま出力の一部として返してしまっている。

**How to apply:** AiService.csでエージェント出力を処理する際、"System:"で始まるプレフィックスや、システムプロンプトの内容が返信冒頭に含まれていないか検証・除去するロジックを追加する必要がある。また、全エージェントが記憶へアクセスできるよう実装を統一することも検討が必要。