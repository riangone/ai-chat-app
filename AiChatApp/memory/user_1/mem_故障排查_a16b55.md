---
name: 故障排查,情绪分析图表,dashboard.yml
description: 情绪分析图表的 SQL 查询配置在 dashboard.yml 中。排查图表未显示数据问题时发现后端接口 GET /diary-companion/Dashbo...
type: user
userId: 1
tags: 故障排查,情绪分析图表,dashboard.yml
relations: 情绪分析图表,dashboard.yml,diary-companion.db
relevanceScore: 80
accessCount: 0
createdAt: 2026-07-02T08:24:15.4110477Z
lastAccessedAt: 2026-07-02T08:24:15.4110478Z
boundAgentRole: 
---

情绪分析图表的 SQL 查询配置在 dashboard.yml 中。排查图表未显示数据问题时发现后端接口 GET /diary-companion/Dashboard 返回正常，怀疑是浏览器端缓存或 JS 报错导致 Chart.js 未执行。