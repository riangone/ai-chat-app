---
name: bug,style,blog,code_block
description: 1. 博客文章代码块样式问题：博客文章中的代码块样式存在未正确匹配的问题，需要确认并修复。
2. NetYamlForge 本地化失效问题：在 NetYamlF...
type: user
userId: 1
tags: bug,style,blog,code_block
relations: 博客文章,NetYamlForge,Controllers/LocalizationController.cs,Settings.cshtml
relevanceScore: 90
accessCount: 5
createdAt: 2026-07-02T23:13:31.0943486Z
lastAccessedAt: 2026-07-03T03:32:43.0388143Z
boundAgentRole: 
---

1. 博客文章代码块样式问题：博客文章中的代码块样式存在未正确匹配的问题，需要确认并修复。
2. NetYamlForge 本地化失效问题：在 NetYamlForge 项目中，用户反馈选择日语后 AI 评估仍显示为中文。根本原因为 Controllers/LocalizationController.cs 中的 LocalizationController.SetLanguage 将语言 Cookie 设置为 HttpOnly=true，导致 Settings.cshtml 通过前端 JavaScript 无法读取 document.cookie，使下拉选择框回退为 zh-CN。修复方案为在 Settings.cshtml 中改用服务端渲染的 @System.Globalization.CultureInfo.CurrentUICulture.Name 来初始化下拉框。