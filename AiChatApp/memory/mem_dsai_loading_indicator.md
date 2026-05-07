---
name: dsai,ui,htmx,loading-indicator,ux
description: dsai项目为AI处理时的主题卡片点击添加了canvas-loading覆盖层加载指示器
type: project
---

dsai项目在base.html中添加了#canvas-loading覆盖层作为AI处理时的加载指示器（显示旋转器和"AIが分析中..."提示，带backdrop-blur效果），并为所有主题卡片按钮添加了hx-indicator="#canvas-loading"属性。此前点击卡片后无任何视觉反馈，用户体验不佳。

**Why:** 用户反馈点击户外/城市/节能卡片后无loading提示，感觉完全没有反应。
**How to apply:** 如需在dsai项目中添加新的AI触发按钮，应同样添加hx-indicator="#canvas-loading"属性。
