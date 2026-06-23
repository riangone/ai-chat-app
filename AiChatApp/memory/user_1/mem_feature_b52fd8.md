---
name: feature,dir_scan,import_jobs,worker
description: 目录扫描（dir_scan）使用 `_SectionForm.cshtml` 渲染表单。点击开始扫描后，任务会写入 `import_jobs` 数据库表，由 W...
type: user
userId: 1
tags: feature,dir_scan,import_jobs,worker
relations: _SectionForm.cshtml,import_jobs
relevanceScore: 42
accessCount: 0
createdAt: 2026-06-20T15:01:42.2271945Z
lastAccessedAt: 2026-06-20T15:01:42.2271947Z
boundAgentRole: 
---

目录扫描（dir_scan）使用 `_SectionForm.cshtml` 渲染表单。点击开始扫描后，任务会写入 `import_jobs` 数据库表，由 Worker 异步处理且不复制文件，仅保存原始路径。