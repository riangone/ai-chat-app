---
name: bug_fix,swagger
description: 已合并的 bug 修复记录如下：
1. 修复了 DynamicEntitySwaggerFilter.cs 中 /api/{project}/{entity}/...
type: user
userId: 1
tags: bug_fix,swagger
relations: DynamicEntitySwaggerFilter.cs,FormValueValidationService,ApiEntityController,Swagger,Database,ApiToken
relevanceScore: 51
accessCount: 0
createdAt: 2026-06-12T12:34:22.4858830Z
lastAccessedAt: 2026-06-12T12:34:35.9651871Z
boundAgentRole: 
---

已合并的 bug 修复记录如下：
1. 修复了 DynamicEntitySwaggerFilter.cs 中 /api/{project}/{entity}/{id} 路径因变量写错而未注册到 Swagger 文档的 Bug。
2. 修复了 FormValueValidationService 会将表单中未提供的字段写入 null，导致插入数据库时覆盖 DEFAULT 0 进而触发 'NOT NULL constraint failed' 约束冲突的 Bug。
3. 修复了 ApiEntityController 的 Bearer Token 认证返回 401 错误的问题，通过显式指定 [Authorize(AuthenticationSchemes = "Cookies,ApiToken")] 成功激活并应用了 ApiToken 认证方案。