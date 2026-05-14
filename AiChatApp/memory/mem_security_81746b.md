---
name: security,csrf,htmx,nyf,dotnet
description: 在 nyf 分支上完成了 CSRF 安全强化，通过在 _Layout.cshtml 添加 Meta 标签及 site.js 配置 HTMX 全局拦截器，确保 A...
type: user
userId: 1
tags: security,csrf,htmx,nyf,dotnet
relations: jpcs,HTMX,nyf,CSRF
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T06:35:15.4341236Z
lastAccessedAt: 2026-05-14T06:35:15.4341237Z
---

在 nyf 分支上完成了 CSRF 安全强化，通过在 _Layout.cshtml 添加 Meta 标签及 site.js 配置 HTMX 全局拦截器，确保 AJAX 请求自动携带安全令牌，并修复了 PageController 的校验问题。