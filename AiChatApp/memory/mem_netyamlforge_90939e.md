---
name: netyamlforge,bugfix,static-assets,razor,pathbase
description: 修复了 NetYamlForge 中所有硬编码绝对路径（/lib/...）导致的静态资源加载失败问题：将 _Layout.cshtml、子项目布局文件中的 /l...
type: user
userId: 1
tags: netyamlforge,bugfix,static-assets,razor,pathbase
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-30T11:18:39.7074719Z
lastAccessedAt: 2026-04-30T11:18:39.7074720Z
---

修复了 NetYamlForge 中所有硬编码绝对路径（/lib/...）导致的静态资源加载失败问题：将 _Layout.cshtml、子项目布局文件中的 /lib/ 路径改为 ~/lib/，利用 ASP.NET Core Tag Helper 自动处理 PathBase