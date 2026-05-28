---
name: technical-debt,runtime-compilation,C#
description: 识别并解决了 `ProjectHookLoader` 运行时编译失败的问题，原因为生成的代码缺少 `using System;` 和 `using System...
type: user
userId: 1
tags: technical-debt,runtime-compilation,C#
relations: ProjectHookLoader,AutoDealerDemoMissingActionHandlers.cs,AutoDealerDemoMissingHooks.cs
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-28T10:08:15.8138465Z
lastAccessedAt: 2026-05-28T10:08:15.8138466Z
---

识别并解决了 `ProjectHookLoader` 运行时编译失败的问题，原因为生成的代码缺少 `using System;` 和 `using System.Threading.Tasks;` 命名空间引用。