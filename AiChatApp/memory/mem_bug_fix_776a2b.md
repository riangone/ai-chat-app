---
name: bug-fix,security,configuration,aspnetcore
description: 修复了 PageController 中的 AntiforgeryValidationException，原因在于项目 PathBase 设置为 /nyf 导致...
type: user
userId: 1
tags: bug-fix,security,configuration,aspnetcore
relations: NetYamlForge,PageController
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T06:33:07.0511792Z
lastAccessedAt: 2026-05-14T06:33:07.0511793Z
---

修复了 PageController 中的 AntiforgeryValidationException，原因在于项目 PathBase 设置为 /nyf 导致 CSRF Cookie 路径不匹配；解决方案是将 Antiforgery Cookie 路径显式设置为 /。