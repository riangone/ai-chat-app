---
name: netyamlforge,ai-assistant,javascript,pathbase
description: 修改了 NetYamlForge/wwwroot/js/ai-assistant.js，新增 getPath() 辅助函数，通过 window.NetYamlF...
type: user
userId: 1
tags: netyamlforge,ai-assistant,javascript,pathbase
relevanceScore: 80
accessCount: 0
createdAt: 2026-04-30T11:18:39.7618312Z
lastAccessedAt: 2026-04-30T11:18:39.7618313Z
---

修改了 NetYamlForge/wwwroot/js/ai-assistant.js，新增 getPath() 辅助函数，通过 window.NetYamlForgeConfig.pathBase 动态构造 apiBaseUrl 和 signalRUrl，解决子路径部署时 API 请求路径错误的问题