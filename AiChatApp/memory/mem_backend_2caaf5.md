---
name: backend,architecture,cli,dependency_injection
description: CLI execution is abstracted via an ICliExecutor interface, which is injected int...
type: user
userId: 1
tags: backend,architecture,cli,dependency_injection
relations: ICliExecutor,AiService,Singleton,dependency injection
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-18T08:24:30.0643863Z
lastAccessedAt: 2026-05-18T08:24:30.0643865Z
---

CLI execution is abstracted via an ICliExecutor interface, which is injected into AiService and registered as a Singleton.