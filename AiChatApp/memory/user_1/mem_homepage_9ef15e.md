---
name: homepage,pagination,lazy_load,_SectionFancyDiaryList.cshtml
description: 主页卡片流目前尚未实现分页功能或滚动懒加载，在渲染组件 _SectionFancyDiaryList.cshtml 中进行一次性全量加载并循环渲染。目前其最大显...
type: user
userId: 1
tags: homepage,pagination,lazy_load,_SectionFancyDiaryList.cshtml
relations: 主页卡片流,_SectionFancyDiaryList.cshtml,HomePage.yaml,diary_list,pageSize
relevanceScore: 30
accessCount: 9
createdAt: 2026-06-28T13:03:36.9336385Z
lastAccessedAt: 2026-07-01T04:06:03.1887950Z
boundAgentRole: 
---

主页卡片流目前尚未实现分页功能或滚动懒加载，在渲染组件 _SectionFancyDiaryList.cshtml 中进行一次性全量加载并循环渲染。目前其最大显示数量限制为 100 篇日记，该数量由 HomePage.yaml 配置文件中 diary_list 组件的 pageSize 属性控制。