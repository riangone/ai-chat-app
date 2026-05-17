---
name: bug,aspnetcore,security
description: 项目 NetYamlForge 在执行 DynamicEntityController.Edit 的 POST 请求时遇到了 AntiforgeryValida...
type: user
userId: 1
tags: bug,aspnetcore,security
relations: NetYamlForge,DynamicEntityController,Antiforgery Token
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T05:02:53.2175060Z
lastAccessedAt: 2026-05-15T05:02:53.2175061Z
---

项目 NetYamlForge 在执行 DynamicEntityController.Edit 的 POST 请求时遇到了 AntiforgeryValidationException 错误，原因是缺少防伪令牌。