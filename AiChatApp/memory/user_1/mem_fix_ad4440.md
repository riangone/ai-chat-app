---
name: fix,routing,FormForgeController,FormBuilder
description: 在 FormForgeController.cs 中添加了 [HttpGet("Create")] 并映射到 CreateGet() 方法，实现点击导航栏 'C...
type: user
userId: 1
tags: fix,routing,FormForgeController,FormBuilder
relations: FormForgeController.cs,CreateGet(),FormBuilder
relevanceScore: 80
accessCount: 1
createdAt: 2026-06-23T13:03:14.7345131Z
lastAccessedAt: 2026-06-23T14:45:49.0867018Z
boundAgentRole: 
---

在 FormForgeController.cs 中添加了 [HttpGet("Create")] 并映射到 CreateGet() 方法，实现点击导航栏 'Create Form' 即可自动创建新表单并重定向到 FormBuilder 编辑器。