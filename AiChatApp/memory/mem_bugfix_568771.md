---
name: bugfix, mindmap, mermaid, backend, security
description: 彻底修复了 Mindmap 渲染错误，通过在 MemoryGraphService.cs 中实施 SanitizeLabel 方法，对节点 ID 和标签进行脱敏...
type: user
userId: 1
tags: bugfix, mindmap, mermaid, backend, security
relations: MemoryGraphService.cs,Mermaid.js
relevanceScore: 80
accessCount: 1
createdAt: 2026-05-12T22:02:48.5235609Z
lastAccessedAt: 2026-05-13T23:07:03.6269141Z
---

彻底修复了 Mindmap 渲染错误，通过在 MemoryGraphService.cs 中实施 SanitizeLabel 方法，对节点 ID 和标签进行脱敏、转义及特殊字符过滤（如 [ ] { } < > |）。