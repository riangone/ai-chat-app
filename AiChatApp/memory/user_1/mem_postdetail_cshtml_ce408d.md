---
name: PostDetail.cshtml,mermaid,css,layout
description: 在 PostDetail.cshtml 中，针对代码块与图表的渲染逻辑进行了以下优化与调整：
1. 引入了 mermaid.min.js 以支持 Mermaid...
type: user
userId: 1
tags: PostDetail.cshtml,mermaid,css,layout
relations: PostDetail.cshtml,mermaid.min.js,Mermaid,YAML
relevanceScore: 90
accessCount: 0
createdAt: 2026-07-02T23:32:48.5174673Z
lastAccessedAt: 2026-07-02T23:32:55.6158495Z
boundAgentRole: 
---

在 PostDetail.cshtml 中，针对代码块与图表的渲染逻辑进行了以下优化与调整：
1. 引入了 mermaid.min.js 以支持 Mermaid 流程图的渲染。为了规避 Mac 风格代码框背景及圆点样式的干扰，JavaScript 逻辑会自动将 `<pre><code class="language-mermaid">` 重构为独立的 `<div class="mermaid">`，并引入了专属图表卡片样式以支持移动端的横向滚动。
2. 优化了 YAML 语言标签的展示，JS 渲染逻辑会自动将 `language-yml` 或 `yml` 类名转换为 `language-yaml`，从而将代码框右上角的语言标签统一显示为 `yaml`。