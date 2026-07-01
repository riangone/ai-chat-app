---
name: dsai htmx error handling
description: dsai项目在base.html中添加了HTMX错误处理逻辑：监听`htmx:responseError`事件，当后端AI（opencode CLI）调用失败返...
type: user
userId: 0
tags: dsai htmx error handling
relations: relevanceScore: 80
relevanceScore: 21
accessCount: 4
createdAt: 2026-05-01T12:14:37.9881485Z
lastAccessedAt: 2026-06-25T03:47:22.6836726Z
boundAgentRole: 
---

dsai项目在base.html中添加了HTMX错误处理逻辑：监听`htmx:responseError`事件，当后端AI（opencode CLI）调用失败返回HTTP错误时，弹出用户可见的错误提示（alert或toast），避免用户在点击卡片后感觉"完全没有反应"。

**Why:** opencode CLI调用可能失败（超时/语法错误），HTMX默认静默失败，用户无法感知。
**How to apply:** 如果dsai卡片点击无反应且无loading，先检查浏览器F12控制台是否有htmx:responseError事件触发，再检查后端日志。