---
name: 系统架构,国际化,i18n
description: 本系统架构包含以下核心技术实现与机制：
1. 国际化与界面多语言支持：系统所有UI文本内容支持根据系统语言自动切换。已通过 i18n.yml、导航栏 label...
type: user
userId: 1
tags: 系统架构,国际化,i18n
relations: i18n,PageDataQueryService,Quiz,PhotoEntry,Outbox,IOutboxJobService,OutboxJobBackgroundService
relevanceScore: 90
accessCount: 0
createdAt: 2026-07-02T14:13:34.3457480Z
lastAccessedAt: 2026-07-02T14:13:42.4650687Z
boundAgentRole: 
---

本系统架构包含以下核心技术实现与机制：
1. 国际化与界面多语言支持：系统所有UI文本内容支持根据系统语言自动切换。已通过 i18n.yml、导航栏 labelKey 机制，以及在 PageDataQueryService 的 SQL 查询中引入 @currentLanguage 参数来实现数据和界面的多语言国际化。
2. 单词挑战（Quiz）模块：后端通过 pages/Quiz.yaml 的 recent_words 联查了 PhotoEntry 表以获取缩略图（PhotoThumbnail）和标注（PhotoCaption）；前端根据系统语言过滤并去重展示图片列表。
3. 消息与任务队列：系统集成了成熟的 Outbox 持久化后台队列机制，主要对应 IOutboxJobService 与 OutboxJobBackgroundService。