---
name: email-chat, bug, AiEmailChatExecutor.cs, gemini-cli, stdout
description: A bug in AiEmailChatExecutor.cs caused it to capture Gemini CLI's diagnostic and...
type: user
userId: 1
tags: email-chat, bug, AiEmailChatExecutor.cs, gemini-cli, stdout
relations: AiEmailChatExecutor.cs,Gemini CLI,email-chat
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-21T14:48:44.3844199Z
lastAccessedAt: 2026-05-21T14:48:44.3844201Z
---

A bug in AiEmailChatExecutor.cs caused it to capture Gemini CLI's diagnostic and error logs from stdout as the actual AI email response body, exposing internal CLI errors to users.