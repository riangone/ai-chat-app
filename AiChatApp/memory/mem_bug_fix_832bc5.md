---
name: bug_fix,javascript
description: 修正了从 Mermaid 节点 ID 中提取原始 ID 的逻辑，通过 `parts.slice(1, -1).join('-')` 过滤掉 Mermaid 自动...
type: user
userId: 1
tags: bug_fix,javascript
relations: Mermaid.js
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T22:28:11.5893627Z
lastAccessedAt: 2026-05-14T22:28:11.5893628Z
---

修正了从 Mermaid 节点 ID 中提取原始 ID 的逻辑，通过 `parts.slice(1, -1).join('-')` 过滤掉 Mermaid 自动生成的序号后缀。