---
name: security,refactoring,best-practices
description: 对 NetYamlForge 的多个关键控制器（Account, DynamicEntity, BatchJob, Localization, Users）进行...
type: user
userId: 1
tags: security,refactoring,best-practices
relations: NetYamlForge,AccountController,DynamicEntityController,BatchJobController,LocalizationController,UsersController
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T06:33:07.1429424Z
lastAccessedAt: 2026-05-14T06:33:07.1429425Z
---

对 NetYamlForge 的多个关键控制器（Account, DynamicEntity, BatchJob, Localization, Users）进行了安全加固，为所有 POST 操作添加了 [ValidateAntiForgeryToken] 属性。