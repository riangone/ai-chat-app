---
name: bugfix,Razor,select
description: 已整合的 Bug 修复记录如下：
1. 修复了 select 类型字段被错误渲染为 text input 的问题：通过在 _SectionInteractive...
type: user
userId: 1
tags: bugfix,Razor,select
relations: _SectionInteractiveForm.cshtml,ProjectApiController.cs
relevanceScore: 76
accessCount: 18
createdAt: 2026-07-01T06:21:33.7149000Z
lastAccessedAt: 2026-07-01T23:54:00.5060557Z
boundAgentRole: 
---

已整合的 Bug 修复记录如下：
1. 修复了 select 类型字段被错误渲染为 text input 的问题：通过在 _SectionInteractiveForm.cshtml 中针对 fieldDef.Type == "select" 或含有 Options 的字段新增渲染分支，使该修复对所有带 options 的字段生效。
2. 修复了 ProjectApiController.cs:83 中的角色 DELETE 接口 bug：原因为缺少 .DisableAntiforgery() 导致删除时返回 400 错误，现已通过添加该配置予以修复。