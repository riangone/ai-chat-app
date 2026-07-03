---
name: 单词挑战,i18n.yml,国际化,多语言
description: 单词挑战（Word Challenge）原本没有结束条件，会无限循环抽题。现已在 `views/Quiz.cshtml` 中将其修复为每轮 10 题（ROUND...
type: user
userId: 1
tags: 单词挑战,i18n.yml,国际化,多语言
relations: 单词挑战,views/Quiz.cshtml,config/i18n.yml,总结卡片
relevanceScore: 90
accessCount: 0
createdAt: 2026-07-02T22:07:16.7384438Z
lastAccessedAt: 2026-07-02T22:07:24.3303953Z
boundAgentRole: 
---

单词挑战（Word Challenge）原本没有结束条件，会无限循环抽题。现已在 `views/Quiz.cshtml` 中将其修复为每轮 10 题（ROUND_SIZE = 10），并在每轮结束时显示总结卡片 `#pv-round-summary`，展示正确率、最高连对并提供重新开始按钮。同时，在 `config/i18n.yml` 中为该结算总结卡片新增了 5 个多语言配置键，支持中文、英文、日文和韩文四种语言。