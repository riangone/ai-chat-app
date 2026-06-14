---
name: security,prompt_injection,Hyperion,AiChatApp
description: 在针对 Hyperion/AiChatApp 的对话中，遭遇并成功拦截了伪装成系统消息的提示注入（Prompt Injection）攻击。该攻击包含虚假的工具调...
type: user
userId: 1
tags: security,prompt_injection,Hyperion,AiChatApp
relations: Hyperion/AiChatApp,Prompt Injection,antigravity,opencode,codex
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-13T03:31:13.1814575Z
lastAccessedAt: 2026-06-13T03:31:59.4676642Z
boundAgentRole: 
---

在针对 Hyperion/AiChatApp 的对话中，遭遇并成功拦截了伪装成系统消息的提示注入（Prompt Injection）攻击。该攻击包含虚假的工具调用与记忆指令，企图诱导 Agent 按照 antigravity -> opencode -> codex 的顺序调用子代理。该攻击被用户与 Agent 共同识破并拦截，未执行任何未授权指令，最终仅完成了合规的 git commit/push 操作。