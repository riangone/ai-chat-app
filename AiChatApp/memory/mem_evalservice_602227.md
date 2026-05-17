---
name: EvalService, Token 浪费, 效率问题
description: EvalService 在每个 AgentStep 后发起 4 次独立的 AI 调用（准确度、安全性、格式、有用性），被识别为最严重的 Token 浪费源。
type: user
userId: 1
tags: EvalService, Token 浪费, 效率问题
relations: AiChatApp,EvalService
relevanceScore: 80
accessCount: 5
createdAt: 2026-05-17T01:46:01.9237384Z
lastAccessedAt: 2026-05-17T02:45:57.3270407Z
---

EvalService 在每个 AgentStep 后发起 4 次独立的 AI 调用（准确度、安全性、格式、有用性），被识别为最严重的 Token 浪费源。