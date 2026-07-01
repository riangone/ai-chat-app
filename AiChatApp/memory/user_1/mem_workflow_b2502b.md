---
name: Workflow,RowMutationRepository,state machine
description: 工作流状态机引擎（IWorkflowEngine）在 RowMutationRepository.cs 的 UpdateAsync 中实现状态校验和流转触发，避...
type: user
userId: 1
tags: Workflow,RowMutationRepository,state machine
relations: IWorkflowEngine,RowMutationRepository.cs,WorkflowEngine.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-30T23:46:25.4788858Z
lastAccessedAt: 2026-06-30T23:46:25.4788859Z
boundAgentRole: 
---

工作流状态机引擎（IWorkflowEngine）在 RowMutationRepository.cs 的 UpdateAsync 中实现状态校验和流转触发，避免了 SQL 拼接的编译规则警告。