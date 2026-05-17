---
name: EvalService, performance, bottleneck, AI-calls
description: EvalService 目前的实现存在性能问题：它为每个维度的评估单独调用一次 AI (ExecuteCliDirectAsync)，导致在多 Agent 流程...
type: user
userId: 1
tags: EvalService, performance, bottleneck, AI-calls
relations: ExecuteCliDirectAsync
relevanceScore: 80
accessCount: 4
createdAt: 2026-05-17T01:54:43.1687066Z
lastAccessedAt: 2026-05-17T02:45:57.3269075Z
---

EvalService 目前的实现存在性能问题：它为每个维度的评估单独调用一次 AI (ExecuteCliDirectAsync)，导致在多 Agent 流程中产生大量冗余的后台 AI 调用。