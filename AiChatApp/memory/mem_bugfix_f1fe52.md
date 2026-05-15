---
name: bugfix, logging, filtering, AiService, gemini-cli
description: Updated AiService.cs with a refined ShouldIgnoreStderrLine logic to filter out n...
type: user
userId: 1
tags: bugfix, logging, filtering, AiService, gemini-cli
relations: AiService,gemini-cli
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T23:50:46.5905740Z
lastAccessedAt: 2026-05-14T23:50:46.5905741Z
---

Updated AiService.cs with a refined ShouldIgnoreStderrLine logic to filter out non-fatal gemini CLI warnings related to terminal colors, YOLO mode, and Node.js deprecations.