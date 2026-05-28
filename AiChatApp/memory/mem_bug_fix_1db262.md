---
name: bug-fix, routing, DetailPage, InvokeAction
description: 修复了详情页（DetailPage.cshtml）中“承认”（Approve）按钮点击报错的问题，原因是按钮指向了不存在的后端动作 ExecuteAction，...
type: user
userId: 1
tags: bug-fix, routing, DetailPage, InvokeAction
relations: DetailPage.cshtml,InvokeAction,DynamicEntityController
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-28T08:42:56.3419079Z
lastAccessedAt: 2026-05-28T08:42:56.3419080Z
---

修复了详情页（DetailPage.cshtml）中“承认”（Approve）按钮点击报错的问题，原因是按钮指向了不存在的后端动作 ExecuteAction，现已修正为 InvokeAction 并统一了参数命名。