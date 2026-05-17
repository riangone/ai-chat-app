---
name: fix,razor,security
description: 通过在 _Form.cshtml 的页面模式和模态框模式表单中添加 @Html.AntiForgeryToken() 解决了 POST 动作的验证失败问题。
type: user
userId: 1
tags: fix,razor,security
relations: _Form.cshtml,Edit Action
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-15T05:02:53.2215390Z
lastAccessedAt: 2026-05-15T05:02:53.2215391Z
---

通过在 _Form.cshtml 的页面模式和模态框模式表单中添加 @Html.AntiForgeryToken() 解决了 POST 动作的验证失败问题。