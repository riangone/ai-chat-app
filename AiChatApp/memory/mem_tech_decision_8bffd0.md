---
name: tech-decision, htmx, best-practices
description: 制定了技术规范：在 HTMX 初始加载或 URL 未发生变化时，应使用 HX-Replace-Url 响应头代替 HX-Push-Url，以避免在浏览器历史记录...
type: user
userId: 1
tags: tech-decision, htmx, best-practices
relations: HX-Replace-Url,HX-Push-Url,HTMX
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T01:44:51.1104181Z
lastAccessedAt: 2026-05-14T01:44:51.1104182Z
---

制定了技术规范：在 HTMX 初始加载或 URL 未发生变化时，应使用 HX-Replace-Url 响应头代替 HX-Push-Url，以避免在浏览器历史记录中产生重复条目。