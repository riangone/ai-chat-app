---
name: Razor, ASP.NET Core, 编译错误, 语法冲突, 最佳实践
description: 在 Razor 视图中，以 'page' 开头的变量名（如 @pageUrl 或 @page.Value.Title）会被误解析为 @page 指令，导致编译错...
type: user
userId: 1
tags: Razor, ASP.NET Core, 编译错误, 语法冲突, 最佳实践
relations: Razor,@page 指令,_Layout.cshtml
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-13T07:28:15.8941845Z
lastAccessedAt: 2026-05-13T07:28:15.8941846Z
---

在 Razor 视图中，以 'page' 开头的变量名（如 @pageUrl 或 @page.Value.Title）会被误解析为 @page 指令，导致编译错误。解决方法是使用显式表达式语法，例如 @(pageUrl) 或 @(page.Value.Title) 来消除歧义。