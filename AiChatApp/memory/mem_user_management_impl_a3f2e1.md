---
name: user_management_implementation
description: 用户管理系统已完成以下实现改进：

1. **User模型提取** → `Models/User.cs`（含 Email、CreatedAt、LastLogin...
type: user
userId: 0
tags: user_management_implementation
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 72
createdAt: 2026-04-30T07:46:32.6112088Z
lastAccessedAt: 2026-05-14T10:14:18.8556189Z
---

用户管理系统已完成以下实现改进：

1. **User模型提取** → `Models/User.cs`（含 Email、CreatedAt、LastLoginAt、IsActive、IsAdmin 字段）
2. **登录逻辑增强** → `AuthEndpoints.cs:38-45`，记录最后登录时间，检查 IsActive 状态
3. **个人资料端点** → `GET/PUT /api/auth/profile`（`AuthEndpoints.cs:56-80`）
4. **管理员策略** → `ServiceExtensions.cs:41-44` 添加 `AdminOnly` 授权策略
5. **管理员端点** → `GET /api/admin/users`、`PUT /api/admin/users/{id}/toggle-active`、`PUT /api/admin/users/{id}/toggle-admin`

**数据隔离现状**：
- ChatSession / Message / LongTermMemory / Note / TodoItem / InputHistory / Project → 均已实施 UserId 隔离

**待改进**：
- SkillEndpoints 未做用户隔离（`SkillEndpoints.cs`）
- StatsEndpoints 未做用户隔离（`StatsEndpoints.cs`）——管理员应看全部，普通用户只看自己的
- 前端缺少用户个人资料页面和管理员界面

**Why:** 用户管理是多用户系统的基础安全需求。
**How to apply:** 涉及 Skills、Stats 端点时，应优先补全用户数据隔离逻辑。