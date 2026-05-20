---
name: bugfix, json_leak, CliExecutorService
description: Fixed a JSON leak bug in CliExecutorService where raw metadata (stats, session_i...
type: user
userId: 1
tags: bugfix, json_leak, CliExecutorService
relations: CliExecutorService
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-20T03:35:45.8810656Z
lastAccessedAt: 2026-05-20T03:35:45.8810657Z
---

Fixed a JSON leak bug in CliExecutorService where raw metadata (stats, session_id) was displayed to the user instead of Markdown content.