---
name: concurrency, thread-safety, bug
description: AiService 中的 PolicyCache 使用静态变量且缺乏锁保护，在多租户并发场景下存在竞态风险。
type: user
userId: 1
tags: concurrency, thread-safety, bug
relations: AiService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-17T02:55:11.5446041Z
lastAccessedAt: 2026-05-17T02:55:11.5446043Z
---

AiService 中的 PolicyCache 使用静态变量且缺乏锁保护，在多租户并发场景下存在竞态风险。