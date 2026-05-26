---
name: bugfix, finance, calculation
description: 修复了股息率（dividendYield）单位显示错误：若 yfinance 返回值大于 1 则自动除以 100 进行比例标准化，防止 UI 显示如 335% ...
type: user
userId: 1
tags: bugfix, finance, calculation
relations: AiChatApp
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T10:20:04.8867526Z
lastAccessedAt: 2026-05-25T10:20:04.8867528Z
---

修复了股息率（dividendYield）单位显示错误：若 yfinance 返回值大于 1 则自动除以 100 进行比例标准化，防止 UI 显示如 335% 的错误结果。