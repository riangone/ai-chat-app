---
name: Webhook,BackgroundService,RowMutationRepository
description: WebhookOutboxPoller.cs 作为后台轮询服务（BackgroundService / HostedService）在 DI 容器中注册，Row...
type: user
userId: 1
tags: Webhook,BackgroundService,RowMutationRepository
relations: WebhookOutboxPoller.cs,RowMutationRepository.cs
relevanceScore: 80
accessCount: 3
createdAt: 2026-06-30T23:46:25.4807450Z
lastAccessedAt: 2026-07-01T01:25:12.6811039Z
boundAgentRole: 
---

WebhookOutboxPoller.cs 作为后台轮询服务（BackgroundService / HostedService）在 DI 容器中注册，RowMutationRepository.cs 里的实体 CRUD 变更会在事务成功后安全写入 Webhook 出发信箱。