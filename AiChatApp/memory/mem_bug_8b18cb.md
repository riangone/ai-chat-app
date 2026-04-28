---
name: bug,AiService,StripEchoedPromptPrefix,Refactoring
description: AiService.cs 中的 StripEchoedPromptPrefix 方法存在误删风险，如果 AI 回复内容以硬编码前缀（如 'Output:'）开头...
type: user
userId: 1
tags: bug,AiService,StripEchoedPromptPrefix,Refactoring
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-28T23:21:32.2636158Z
lastAccessedAt: 2026-04-28T23:21:32.2636159Z
---

AiService.cs 中的 StripEchoedPromptPrefix 方法存在误删风险，如果 AI 回复内容以硬编码前缀（如 'Output:'）开头或与前缀在同一行，会被错误裁切。