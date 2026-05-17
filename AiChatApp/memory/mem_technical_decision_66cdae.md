---
name: technical_decision, markdown, commonmark, yaml, bugfix
description: 技术决策：在 PostDetail.yaml 中，为了符合 CommonMark 规范，在 HTML 块（如 </div>）与 Markdown 内容之间显式增...
type: user
userId: 1
tags: technical_decision, markdown, commonmark, yaml, bugfix
relations: PostDetail.yaml,CommonMark,markdown
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T04:43:59.1485449Z
lastAccessedAt: 2026-05-15T04:43:59.1485449Z
---

技术决策：在 PostDetail.yaml 中，为了符合 CommonMark 规范，在 HTML 块（如 </div>）与 Markdown 内容之间显式增加了两个换行符（CHAR(10)），以确保 Markdown 解析器能正确识别并渲染后续内容。