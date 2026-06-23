---
name: bug,routing,navigation,project.yaml
description: 在 project.yaml 中，'Create Form' 导航栏链接指向了 GET /form-forge/FormForge/Create，但控制器最初仅...
type: user
userId: 1
tags: bug,routing,navigation,project.yaml
relations: project.yaml,FormForgeController.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-23T13:03:14.7337552Z
lastAccessedAt: 2026-06-23T13:03:14.7337555Z
boundAgentRole: 
---

在 project.yaml 中，'Create Form' 导航栏链接指向了 GET /form-forge/FormForge/Create，但控制器最初仅注册了 [HttpPost("Create")]，导致 GET 请求返回 404 错误。