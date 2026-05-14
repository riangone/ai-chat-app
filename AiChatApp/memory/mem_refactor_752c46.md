---
name: refactor,hardcode,provider,gemini,configuration
description: 将代码库中硬编码的 "gemini" 提供商字符串重构为从配置读取（AiService.DefaultProvider / IConfiguration），涉及...
type: user
userId: 1
tags: refactor,hardcode,provider,gemini,configuration
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 119
createdAt: 2026-04-27T23:00:50.4968439Z
lastAccessedAt: 2026-05-14T01:59:08.3095219Z
---

将代码库中硬编码的 "gemini" 提供商字符串重构为从配置读取（AiService.DefaultProvider / IConfiguration），涉及文件：AiService.cs、ChatEndpoints.cs、EvalService.cs、Models/Message.cs、Models/Harness/PipelineConfig.cs、ServiceExtensions.cs、MemoryFileService.cs