---
name: implementation,heartbeat,timeout_prevention,CliExecutorService
description: 在 CliExecutorService.cs 中实现了跨代理心跳传播与超时续约机制，通过维护全局活跃执行记录字典 _activeExecutions，并结合 ...
type: user
userId: 1
tags: implementation,heartbeat,timeout_prevention,CliExecutorService
relations: CliExecutorService,heartbeat,timeout
relevanceScore: 80
accessCount: 0
createdAt: 2026-07-01T01:06:43.5308062Z
lastAccessedAt: 2026-07-01T01:06:43.5308063Z
boundAgentRole: 
---

在 CliExecutorService.cs 中实现了跨代理心跳传播与超时续约机制，通过维护全局活跃执行记录字典 _activeExecutions，并结合 AsyncLocal<string?> 与环境变量在多个代理间传递状态，解决嵌套调用超时问题。