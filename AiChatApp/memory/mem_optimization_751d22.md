---
name: optimization, csharp, AiService
description: AiService.cs in AiChatApp contains multiple sequential awaits in BuildSystemProm...
type: user
userId: 1
tags: optimization, csharp, AiService
relations: AiService,BuildSystemPromptAsync,Task.WhenAll
relevanceScore: 80
accessCount: 4
createdAt: 2026-05-14T23:17:14.7796271Z
lastAccessedAt: 2026-05-17T02:56:02.0440768Z
---

AiService.cs in AiChatApp contains multiple sequential awaits in BuildSystemPromptAsync (SearchAsync, SearchSkillsAsync, LoadPoliciesAsync, session queries) that should be parallelized using Task.WhenAll.