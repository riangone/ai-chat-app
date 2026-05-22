---
name: architecture,dotnet,dependency-injection
description: 解决了依赖注入 (DI) 生命周期冲突：将 IEmailService 的注册改为 Singleton，以兼容单例运行的 SendEmailHook，防止应用启...
type: user
userId: 1
tags: architecture,dotnet,dependency-injection
relations: IEmailService,SendEmailHook
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-21T23:47:12.7725339Z
lastAccessedAt: 2026-05-21T23:47:12.7725341Z
---

解决了依赖注入 (DI) 生命周期冲突：将 IEmailService 的注册改为 Singleton，以兼容单例运行的 SendEmailHook，防止应用启动崩溃。