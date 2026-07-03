---
name: 平台优化,超时解决,最佳实践
description: 为避免 Antigravity 平台调用超时（Error: timeout waiting for response），需将慢命令（如 dotnet build...
type: user
userId: 1
tags: 平台优化,超时解决,最佳实践
relations: Antigravity,WaitMsBeforeAsync,grep_search
relevanceScore: 72
accessCount: 1
createdAt: 2026-07-01T12:46:59.6095144Z
lastAccessedAt: 2026-07-02T02:14:31.8170625Z
boundAgentRole: 
---

为避免 Antigravity 平台调用超时（Error: timeout waiting for response），需将慢命令（如 dotnet build）转为后台任务运行（设置 WaitMsBeforeAsync，例如 500ms），并在进行 grep_search 时避免广域搜索，显式使用 Includes 进行过滤。