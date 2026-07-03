---
name: antigravity,timeout,Wrapper
description: 用户在调用 antigravity 时可能会遇到 'timeout waiting for response' 的超时错误或无响应问题。这通常是由于长时间运行的...
type: user
userId: 1
tags: antigravity,timeout,Wrapper
relations: antigravity,timeout waiting for response,WaitMsBeforeAsync,Wrapper
relevanceScore: 90
accessCount: 7
createdAt: 2026-07-02T02:17:48.0120998Z
lastAccessedAt: 2026-07-03T01:37:36.2423979Z
boundAgentRole: 
---

用户在调用 antigravity 时可能会遇到 'timeout waiting for response' 的超时错误或无响应问题。这通常是由于长时间运行的的命令（例如 dotnet build 等）阻塞了响应通道。解决此调用超时的方法包括：1. 将耗时命令改为异步或后台执行（例如通过设置 WaitMsBeforeAsync，如 500ms），以避免同步等待其完成；2. 增大客户端 Wrapper 的超时阈值。