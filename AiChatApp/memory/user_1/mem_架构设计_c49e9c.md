---
name: 架构设计,CLI subprocess,订阅服务,本地模型
description: 系统架构采用分层混合设计，保留并优化 CLI 子进程（如 claude, gemini, copilot CLI）以利用用户现有订阅，免除 API 费用；同时对...
type: user
userId: 1
tags: 架构设计,CLI subprocess,订阅服务,本地模型
relations: Hyperion Platform,CLI subprocess,Ollama,LMStudio
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-23T01:53:49.5091534Z
lastAccessedAt: 2026-06-23T01:53:49.5091535Z
boundAgentRole: 
---

系统架构采用分层混合设计，保留并优化 CLI 子进程（如 claude, gemini, copilot CLI）以利用用户现有订阅，免除 API 费用；同时对于本地模型使用直接 SDK 方式。