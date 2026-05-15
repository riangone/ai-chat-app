---
name: memory,agents,copilot,codex,opencode,AiService
description: ユーザーはcopilot、codex、opencodeを含む全エージェントがメモリ機能（SessionMemory・LongTermMemory）を利用すること...
type: user
userId: 1
tags: memory,agents,copilot,codex,opencode,AiService
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 224
createdAt: 2026-04-28T00:57:09.0867641Z
lastAccessedAt: 2026-05-14T23:09:56.5527494Z
---

ユーザーはcopilot、codex、opencodeを含む全エージェントがメモリ機能（SessionMemory・LongTermMemory）を利用することを要求。`BuildSystemPromptAsync`にSessionMemory注入、`RunAgentStepAsync`にLongTermMemory注入を追加してすべてのプロバイダーへ対応済み。