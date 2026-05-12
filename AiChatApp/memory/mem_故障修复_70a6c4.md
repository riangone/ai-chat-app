---
name: 故障修复, 安全, AiService
description: 修复了 AI 回复开头包含系统提示词（泄露）的问题，通过在 AiService.cs 中引入动态提示词注入增强清理逻辑。相关记录见 memory/mem_bug...
type: user
userId: 1
tags: 故障修复, 安全, AiService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-07T23:26:17.8371160Z
lastAccessedAt: 2026-05-07T23:26:17.8371160Z
---

修复了 AI 回复开头包含系统提示词（泄露）的问题，通过在 AiService.cs 中引入动态提示词注入增强清理逻辑。相关记录见 memory/mem_bug_fix_system_prompt_leak.md。