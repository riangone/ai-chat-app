---
name: implementation_detail,aiservice,harness,gemini,tokens
description: 修复 Token 统计涉及对 AiService.cs 和 HarnessEndpoints.cs 的修改，关键动作包括移除 Gemini 调用的 --raw-...
type: user
userId: 1
tags: implementation_detail,aiservice,harness,gemini,tokens
relevanceScore: 80
accessCount: 1
createdAt: 2026-04-29T02:03:32.2946817Z
lastAccessedAt: 2026-04-29T07:20:53.6869924Z
---

修复 Token 统计涉及对 AiService.cs 和 HarnessEndpoints.cs 的修改，关键动作包括移除 Gemini 调用的 --raw-output 参数、更新流式 JSON 捕获逻辑以及重构统计 SQL。