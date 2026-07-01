---
name: technical_detail,routing,DashboardController,NetYamlForge
description: 在 NetYamlForge 项目中，默认路由会将子项目根目录访问（如 `/diary-companion/`）分发到 `DashboardController...
type: user
userId: 1
tags: technical_detail,routing,DashboardController,NetYamlForge
relations: NetYamlForge,DashboardController,HomePage
relevanceScore: 80
accessCount: 35
createdAt: 2026-06-28T01:21:43.4857971Z
lastAccessedAt: 2026-06-30T23:10:18.9331243Z
boundAgentRole: 
---

在 NetYamlForge 项目中，默认路由会将子项目根目录访问（如 `/diary-companion/`）分发到 `DashboardController`。为了直接显示 `Home` 页面，修改了 `DashboardController.cs` 中的 `Index` 方法以支持在该访问路径下重定向或渲染 `HomePage`。