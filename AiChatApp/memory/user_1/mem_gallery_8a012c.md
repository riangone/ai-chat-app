---
name: Gallery, bugfix, paths
description: 修复了 Gallery 页面照片预览无法显示（返回 404）的问题。根本原因为数据库中存的是 Web 相对路径（以 `/uploads/` 开头），而控制器直接...
type: user
userId: 1
tags: Gallery, bugfix, paths
relations: Gallery,PhotoFileController,Database
relevanceScore: 56
accessCount: 0
createdAt: 2026-06-21T02:23:25.0214024Z
lastAccessedAt: 2026-06-21T02:23:25.0214026Z
boundAgentRole: 
---

修复了 Gallery 页面照片预览无法显示（返回 404）的问题。根本原因为数据库中存的是 Web 相对路径（以 `/uploads/` 开头），而控制器直接用作文件系统根路径进行判断。