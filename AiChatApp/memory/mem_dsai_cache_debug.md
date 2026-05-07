---
name: dsai deployment cache debug
description: dsai重启后页面无变化的排查方法：浏览器缓存或curl验证后端
type: project
---

dsai服务重启后如果页面仍无变化，按以下步骤排查：
1. `curl localhost:8002/dsai` 检查后端返回的HTML源码是否包含新内容
2. 如后端已更新但浏览器未变化，为浏览器强缓存问题，强制刷新（Ctrl+Shift+R）
3. 导航栏标题含版本号（如"NEXT-GEN MOTORS v1.2"），可用于快速确认部署版本

**Why:** Jinja2模板每次请求重新渲染（无服务端模板缓存），因此后端重启后内容立即更新；但浏览器可能缓存旧HTML。
**How to apply:** 部署验证时先curl确认后端版本，再检查浏览器是否命中缓存。
