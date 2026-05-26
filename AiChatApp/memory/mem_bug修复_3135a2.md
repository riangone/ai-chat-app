---
name: Bug修复, 前端, 事件处理, opencode
description: 修复了使用 'opencode' 指令或快捷键提交时消息发送两次的问题。原因是 onkeydown 处理器未调用 preventDefault()，导致 Ctr...
type: user
userId: 1
tags: Bug修复, 前端, 事件处理, opencode
relations: opencode,handleFormSubmit,onkeydown
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T10:47:37.4490922Z
lastAccessedAt: 2026-05-25T10:47:37.4490923Z
---

修复了使用 'opencode' 指令或快捷键提交时消息发送两次的问题。原因是 onkeydown 处理器未调用 preventDefault()，导致 Ctrl+Enter 同时触发了脚本提交和表单默认提交。