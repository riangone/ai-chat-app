---
name: EvalService, architecture-flaw, P0
description: EvalService.cs 在每个 AgentStep 完成后独立调用 4 次 AI 进行 Accuracy/Safety/Format/Helpfulnes...
type: user
userId: 1
tags: EvalService, architecture-flaw, P0
relations: EvalService
relevanceScore: 80
accessCount: 1
createdAt: 2026-05-17T02:55:11.5281440Z
lastAccessedAt: 2026-05-17T02:56:02.0456372Z
---

EvalService.cs 在每个 AgentStep 完成后独立调用 4 次 AI 进行 Accuracy/Safety/Format/Helpfulness 评估，建议合并为单次请求。