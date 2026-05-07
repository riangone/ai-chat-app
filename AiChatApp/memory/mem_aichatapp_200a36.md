---
name: AiChatApp,architecture,loading-strategy,HTMX,Swiper
description: AiChatApp 的 Swiper 内容采用混合加载策略：页面加载时预加载所有标签页（Chat, Files, Notes, Settings, Todo 等...
type: user
userId: 1
tags: AiChatApp,architecture,loading-strategy,HTMX,Swiper
relevanceScore: 80
accessCount: 10
createdAt: 2026-04-28T13:51:19.2161558Z
lastAccessedAt: 2026-04-30T07:37:18.8076699Z
---

AiChatApp 的 Swiper 内容采用混合加载策略：页面加载时预加载所有标签页（Chat, Files, Notes, Settings, Todo 等）的列表框架，而具体项的详情内容通过 HTMX 异步按需加载。