---
name: technical,bugfix,htmx,aspnetcore
description: Fixed project deletion failure by adding .DisableAntiforgery() to the DELETE end...
type: user
userId: 1
tags: technical,bugfix,htmx,aspnetcore
relations: ProjectApiController,Project Deletion
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-28T02:19:43.4089748Z
lastAccessedAt: 2026-05-28T02:19:43.4089749Z
---

Fixed project deletion failure by adding .DisableAntiforgery() to the DELETE endpoint in ProjectApiController to support HTMX requests.