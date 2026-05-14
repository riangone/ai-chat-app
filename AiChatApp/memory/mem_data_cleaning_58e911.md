---
name: data-cleaning, memory-graph, logic
description: 在 MemoryGraphService.cs 中增加了 IsValidEntity 校验逻辑，自动剔除包含元数据关键字（如 relevanceScore）、纯...
type: user
userId: 1
tags: data-cleaning, memory-graph, logic
relations: MemoryGraphService.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T22:02:48.5973400Z
lastAccessedAt: 2026-05-12T22:02:48.5973401Z
---

在 MemoryGraphService.cs 中增加了 IsValidEntity 校验逻辑，自动剔除包含元数据关键字（如 relevanceScore）、纯数字或单字符的无效关系节点。