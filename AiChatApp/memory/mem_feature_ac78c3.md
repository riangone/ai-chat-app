---
name: feature,authentication,security
description: 实现了用户认证功能，采用 Cookie 会话管理。Python 环境下使用 itsdangerous 和 passlib[bcrypt]，默认账号密码为 adm...
type: user
userId: 1
tags: feature,authentication,security
relations: Authentication,webui/auth.py,FastAPI
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T09:55:11.4358962Z
lastAccessedAt: 2026-05-25T09:55:11.4358963Z
---

实现了用户认证功能，采用 Cookie 会话管理。Python 环境下使用 itsdangerous 和 passlib[bcrypt]，默认账号密码为 admin / admin123。