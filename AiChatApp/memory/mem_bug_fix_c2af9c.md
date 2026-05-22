---
name: bug-fix,configuration
description: 优化了 .env 配置文件解析器，使其能够正确处理行尾注释（如 AI_MODEL= # Comment），修复了因模型名称解析错误导致的 ModelNotFou...
type: user
userId: 1
tags: bug-fix,configuration
relations: .env 解析器
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-21T23:47:12.7790403Z
lastAccessedAt: 2026-05-21T23:47:12.7790404Z
---

优化了 .env 配置文件解析器，使其能够正确处理行尾注释（如 AI_MODEL= # Comment），修复了因模型名称解析错误导致的 ModelNotFoundError。