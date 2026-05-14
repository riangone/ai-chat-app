---
name: netyamlforge,pathbase,fix,aspnetcore,url-content,program-cs
description: NetYamlForge 路径修复方案：在 Program.cs 中添加兜底中间件（PathBase 为空时强制设为 /nyf）；将所有 Razor 视图中的 ...
type: user
userId: 1
tags: netyamlforge,pathbase,fix,aspnetcore,url-content,program-cs
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 5
createdAt: 2026-04-30T11:33:00.6250676Z
lastAccessedAt: 2026-05-14T00:52:10.0748549Z
---

NetYamlForge 路径修复方案：在 Program.cs 中添加兜底中间件（PathBase 为空时强制设为 /nyf）；将所有 Razor 视图中的 /lib/ 硬编码路径改为 @Url.Content("~/lib/...")；在 _Layout.cshtml 中注入 window.NetYamlForgeConfig.pathBase = "@Url.Content(\"~/\").TrimEnd('/')"；更新 ProjectTemplateScaffolder.cs 模板和所有现有子项目布局文件