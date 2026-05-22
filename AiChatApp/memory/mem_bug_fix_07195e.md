---
name: bug-fix,gemini-cli,dotnet,email-chat
description: 修复了 AI 邮件聊天系统中 Gemini CLI 调用的超时问题。根本原因是 AiEmailChatExecutor.cs 中未设置 RedirectStan...
type: user
userId: 1
tags: bug-fix,gemini-cli,dotnet,email-chat
relations: AI 邮件聊天系统,Gemini CLI,AiEmailChatExecutor.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-21T23:47:12.5977851Z
lastAccessedAt: 2026-05-21T23:47:12.5977851Z
---

修复了 AI 邮件聊天系统中 Gemini CLI 调用的超时问题。根本原因是 AiEmailChatExecutor.cs 中未设置 RedirectStandardInput = true 并手动关闭 stdin，导致 CLI 在非交互模式下无限等待输入。