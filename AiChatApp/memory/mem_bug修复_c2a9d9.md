---
name: Bug修复,Mermaid.js,标签清理
description: 修复了 Mermaid 渲染故障：通过在 MemoryGraphService 中添加 SanitizeLabel 方法，清理了标签中的特殊字符（如 []{}|...
type: user
userId: 1
tags: Bug修复,Mermaid.js,标签清理
relations: MemoryGraphService,Mermaid.js
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-12T22:00:45.1009045Z
lastAccessedAt: 2026-05-12T22:00:45.1009046Z
---

修复了 Mermaid 渲染故障：通过在 MemoryGraphService 中添加 SanitizeLabel 方法，清理了标签中的特殊字符（如 []{}|<> 和双引号），并限制了标签长度。