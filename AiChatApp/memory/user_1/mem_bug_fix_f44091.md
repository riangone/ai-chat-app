---
name: bug-fix,frontend,vector-search
description: _SectionVectorSearch.cshtml 曾发生检索结果未更新的问题，原因为内联脚本在 DOM 加载前执行导致选择器返回 null。修复方式是将 ...
type: user
userId: 1
tags: bug-fix,frontend,vector-search
relations: _SectionVectorSearch.cshtml,getGallery
relevanceScore: 80
accessCount: 0
createdAt: 2026-06-23T06:14:39.0879412Z
lastAccessedAt: 2026-06-23T06:14:39.0879415Z
boundAgentRole: 
---

_SectionVectorSearch.cshtml 曾发生检索结果未更新的问题，原因为内联脚本在 DOM 加载前执行导致选择器返回 null。修复方式是将 gallery 元素查找改为延迟查找（getGallery 函数）。