---
name: dotnet-core,middleware,bug-fix,routing
description: 根目录静态文件（如 service-worker.js, manifest.json）报 404 错误的原因是 ASP.NET Core 路由中间件提前拦截了请...
type: user
userId: 1
tags: dotnet-core,middleware,bug-fix,routing
relations: ASP.NET Core,app.UseStaticFiles(),ServiceWorker
relevanceScore: 76
accessCount: 0
createdAt: 2026-07-02T04:55:28.5980737Z
lastAccessedAt: 2026-07-02T04:55:28.5980738Z
boundAgentRole: 
---

根目录静态文件（如 service-worker.js, manifest.json）报 404 错误的原因是 ASP.NET Core 路由中间件提前拦截了请求，解决方案是将 app.UseStaticFiles() 移至中间件管道的最前端（即所有中间件之前）。