---
name: mindmap,mermaid,MemoryGraphService,bug-fix
description: 修复了思维导图（Mindmap）加载失败的问题。在 MemoryGraphService.cs 中实现了 Mermaid 节点 ID 的脱敏处理（仅允许字母、数...
type: user
userId: 1
tags: mindmap,mermaid,MemoryGraphService,bug-fix
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 4
createdAt: 2026-05-12T10:05:00.2463237Z
lastAccessedAt: 2026-05-13T23:07:03.6267522Z
---

修复了思维导图（Mindmap）加载失败的问题。在 MemoryGraphService.cs 中实现了 Mermaid 节点 ID 的脱敏处理（仅允许字母、数字和下划线）以及标签内容的转义，并将节点匹配逻辑改为精确匹配。