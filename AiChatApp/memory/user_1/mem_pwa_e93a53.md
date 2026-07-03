---
name: PWA,bugfix,static_files,ASP.NET_Core
description: 项目配置了 PWA（渐进式 Web 应用）功能，但曾因 wwwroot 根目录下的 service-worker.js、manifest.json 和 favi...
type: user
userId: 1
tags: PWA,bugfix,static_files,ASP.NET_Core
relations: PWA,app.UseStaticFiles(),service-worker.js,manifest.json
relevanceScore: 90
accessCount: 0
createdAt: 2026-07-02T05:41:43.6107451Z
lastAccessedAt: 2026-07-02T05:42:01.1900213Z
boundAgentRole: 
---

项目配置了 PWA（渐进式 Web 应用）功能，但曾因 wwwroot 根目录下的 service-worker.js、manifest.json 和 favicon.ico 等静态文件返回 404 错误，导致 Service Worker 注册失败，使用户无法在浏览器中看到“安装到主屏幕”的提示或安装选项。该部署与安装问题已通过将 app.UseStaticFiles() 移至中间件管道最前端得到成功修复，目前本地验证这些文件的请求状态码已恢复为 200。