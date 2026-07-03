---
name: bug_fix,sql,diary-companion
description: 在 diary-companion 项目中，日记图片无法显示是因为 HomePage.yaml (diary_list 组件) 和 DiaryForm.yaml...
type: user
userId: 1
tags: bug_fix,sql,diary-companion
relations: HomePage.yaml,DiaryForm.yaml,ImageBase64,ImageLabel
relevanceScore: 72
accessCount: 21
createdAt: 2026-06-28T02:14:41.3138932Z
lastAccessedAt: 2026-07-02T09:09:24.9167288Z
boundAgentRole: 
---

在 diary-companion 项目中，日记图片无法显示是因为 HomePage.yaml (diary_list 组件) 和 DiaryForm.yaml (diary_detail 组件) 的 SQL 查询及 columns 中缺少了 ImageBase64 与 ImageLabel 字段，修复方法是补全这些字段。