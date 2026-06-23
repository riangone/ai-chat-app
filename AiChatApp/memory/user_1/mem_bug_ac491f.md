---
name: bug,photo_annotation,antigravity_cli_worker,processing_queue
description: 在照片标注系统中，目录扫描导入的照片默认以 provider='lmstudio' 写入 processing_queue，而后台 worker (antigr...
type: user
userId: 1
tags: bug,photo_annotation,antigravity_cli_worker,processing_queue
relations: antigravity_cli_worker,processing_queue
relevanceScore: 63
accessCount: 0
createdAt: 2026-06-21T12:15:51.2364076Z
lastAccessedAt: 2026-06-21T12:15:51.2364077Z
boundAgentRole: 
---

在照片标注系统中，目录扫描导入的照片默认以 provider='lmstudio' 写入 processing_queue，而后台 worker (antigravity_cli_worker) 仅查找 provider='antigravity' 的任务，导致标注无法完成。