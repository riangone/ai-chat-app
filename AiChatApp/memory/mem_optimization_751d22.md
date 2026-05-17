---
name: optimization, csharp, AiService
description: AiService.cs in AiChatApp contains multiple sequential awaits in BuildSystemProm...
type: user
userId: 1
tags: optimization, csharp, AiService
relations: AiService,BuildSystemPromptAsync,Task.WhenAll
relevanceScore: 80
accessCount: 6
createdAt: 2026-05-14T23:17:14.7796271Z
lastAccessedAt: 2026-05-17T06:14:25.8140385Z
---

AiService.cs in AiChatApp contains multiple sequential awaits in BuildSystemPromptAsync (SearchAsync, SearchSkillsAsync, LoadPoliciesAsync, session queries) that should be parallelized using Task.WhenAll.