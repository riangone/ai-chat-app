---
name: dsai cookie path session bug
description: dsai项目中"シミュレーションを開始"和"試乗予約"按钮报错的根因是Cookie路径未设置path="/"
type: project
---

dsai项目部署在/dsai子路径下，sessions.py和chat.py中设置session_id Cookie时未显式指定`path="/"`，导致浏览器在不同路径层级（/dsai/ 和 /dsai/api/...）之间无法正确发送Cookie，后端找不到有效会话，返回400/500错误。

修复：在`app/routers/sessions.py`和`app/routers/chat.py`中统一使用`response.set_cookie("session_id", ..., path="/")`。

**Why:** 子路径部署时Cookie默认只对当前路径生效，API子路径不会自动接收主页面设置的Cookie。
**How to apply:** 凡是在子路径（非根路径）部署的FastAPI应用，set_cookie时必须显式设置`path="/"`，否则HTMX的API请求会因缺少Cookie而返回会话错误。
