---
name: refactor,architecture,provider,defaultprovider,pattern
description: 重构策略：模型层（User、ChatSession 等）的 DefaultProvider 字段默认值改为 string.Empty，由服务层从 AiServi...
type: user
userId: 1
tags: refactor,architecture,provider,defaultprovider,pattern
relevanceScore: 80
accessCount: 6
createdAt: 2026-04-27T23:00:50.5015418Z
lastAccessedAt: 2026-04-27T23:46:51.2747285Z
---

重构策略：模型层（User、ChatSession 等）的 DefaultProvider 字段默认值改为 string.Empty，由服务层从 AiService.DefaultProvider 读取配置并处理回退逻辑