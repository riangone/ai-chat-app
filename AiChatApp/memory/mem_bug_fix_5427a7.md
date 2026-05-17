---
name: bug-fix,sql,markdown-rendering
description: PostDetail.yaml 中的 SQL 查询被修改，以在 HTML 头部（</div>）和正文内容之间插入两个换行符 (CHAR(10) || CHAR(...
type: user
userId: 1
tags: bug-fix,sql,markdown-rendering
relations: PostDetail.yaml,CommonMark
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T04:42:20.5317377Z
lastAccessedAt: 2026-05-15T04:42:20.5317378Z
---

PostDetail.yaml 中的 SQL 查询被修改，以在 HTML 头部（</div>）和正文内容之间插入两个换行符 (CHAR(10) || CHAR(10))，修复了标题不渲染的问题。