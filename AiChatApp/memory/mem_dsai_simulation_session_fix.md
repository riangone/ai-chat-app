---
name: dsai simulation session auto-create fix
description: dsai的start_simulation端点在缺少session_id时返回400，已修复为自动创建新会话
type: project
---

dsai项目`app/routers/simulation.py`中的`start_simulation`端点原来在`session_id` Cookie缺失或DB中找不到对应会话时直接返回400，导致首次访问用户点击首页主题按钮（户外/城市/节能）时报"请求处理中发生错误"。

修复：修改`start_simulation`使其在会话缺失或无效时自动创建新会话并设置Cookie后继续处理，不再直接返回400。同时修复`app/routers/appointments.py`：向`appointment_form`模板传递`now_date`变量（原先模板引用了该变量但后端未传递）。

**Why:** 用户首次访问时没有session_id Cookie，过于严格的400检查会阻断正常流程。
**How to apply:** dsai所有需要会话的端点应在会话缺失时自动创建，而非返回4xx错误；模板变量必须与后端传递的context键名一致。
