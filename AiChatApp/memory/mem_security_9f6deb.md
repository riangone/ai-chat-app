---
name: security,htmx,frontend-strategy
description: 在 NetYamlForge 中实现了 HTMX 全局 CSRF 支持：通过 _Layout.cshtml 添加 meta 标签并在 site.js 中利用 h...
type: user
userId: 1
tags: security,htmx,frontend-strategy
relations: NetYamlForge,HTMX
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T06:33:07.0949396Z
lastAccessedAt: 2026-05-14T06:33:07.0949397Z
---

在 NetYamlForge 中实现了 HTMX 全局 CSRF 支持：通过 _Layout.cshtml 添加 meta 标签并在 site.js 中利用 htmx:configRequest 事件自动为所有请求添加 CSRF Token Header。