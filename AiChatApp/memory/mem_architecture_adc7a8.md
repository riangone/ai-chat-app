---
name: architecture, backend, database, proactive suggestions
description: Proactive Suggestions architecture includes database persistence before broadcas...
type: user
userId: 1
tags: architecture, backend, database, proactive suggestions
relations: ProactiveSuggestion,AppDbContext,ProactiveBrainService,Database
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-19T09:45:29.9658584Z
lastAccessedAt: 2026-05-19T09:45:29.9658586Z
---

Proactive Suggestions architecture includes database persistence before broadcasting. The 'ProactiveSuggestion' model tracks 'UserId' and 'IsRead' state, and is managed via 'AppDbContext'.