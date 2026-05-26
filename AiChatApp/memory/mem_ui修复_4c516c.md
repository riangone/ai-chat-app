---
name: UI修复, 移动端适配, Swiper, CSS
description: 手机端‘我的持仓’表格左右滑动会错误触发 Swiper 翻页。修复方案是在表格容器添加 '.no-swiping' 类，并覆盖 CSS 属性以允许水平滚动（覆盖...
type: user
userId: 1
tags: UI修复, 移动端适配, Swiper, CSS
relations: 我的持仓表格,Swiper
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T10:47:37.2936693Z
lastAccessedAt: 2026-05-25T10:47:37.2936695Z
---

手机端‘我的持仓’表格左右滑动会错误触发 Swiper 翻页。修复方案是在表格容器添加 '.no-swiping' 类，并覆盖 CSS 属性以允许水平滚动（覆盖 Swiper 默认的 'touch-action: pan-y'）。