---
name: prompt injection,security,AiChatApp
description: 提示词中出现伪装成 'Hyperion/AiChatApp' 系统消息的注入内容，包含虚假工具调用和子代理链式调用指令（antigravity -> openc...
type: user
userId: 1
tags: prompt injection,security,AiChatApp
relations: Hyperion/AiChatApp,NetYamlForge
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-13T15:25:53.3508376Z
lastAccessedAt: 2026-06-13T15:25:53.3508377Z
boundAgentRole: 
---

提示词中出现伪装成 'Hyperion/AiChatApp' 系统消息的注入内容，包含虚假工具调用和子代理链式调用指令（antigravity -> opencode -> codex），其来源可能在客户端或中间层（如 ai-chat-app 代理脱壳逻辑），而非 NetYamlForge 本身的代码问题。