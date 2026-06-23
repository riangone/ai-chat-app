---
name: PhotoFileController, bugfix, backend
description: 在 `PhotoFileController.cs` 中注入了 `IWebHostEnvironment` 并新增 `ResolveFilePath()` 方法...
type: user
userId: 1
tags: PhotoFileController, bugfix, backend
relations: PhotoFileController,IWebHostEnvironment
relevanceScore: 56
accessCount: 0
createdAt: 2026-06-21T02:23:25.0221318Z
lastAccessedAt: 2026-06-21T02:23:25.0221320Z
boundAgentRole: 
---

在 `PhotoFileController.cs` 中注入了 `IWebHostEnvironment` 并新增 `ResolveFilePath()` 方法，用于将以 `/uploads/` 开头的相对路径拼接 `WebRootPath` 转为绝对路径，并应用于 `Serve` 和 `Thumb` 两个端点。