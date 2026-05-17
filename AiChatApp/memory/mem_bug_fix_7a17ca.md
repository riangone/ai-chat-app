---
name: bug-fix, security, NetYamlForge, ASP.NET-Core
description: 修复了 NetYamlForge 项目中 POST Edit 操作时 AntiForgery 验证失败的问题，具体做法是在 `NetYamlForge/View...
type: user
userId: 1
tags: bug-fix, security, NetYamlForge, ASP.NET-Core
relations: NetYamlForge,AntiForgeryToken,_Form.cshtml
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T05:07:35.5663914Z
lastAccessedAt: 2026-05-15T05:07:35.5663916Z
---

修复了 NetYamlForge 项目中 POST Edit 操作时 AntiForgery 验证失败的问题，具体做法是在 `NetYamlForge/Views/DynamicEntity/_Form.cshtml` 的页面模式和模态框模式中各添加了 `@Html.AntiForgeryToken()`。