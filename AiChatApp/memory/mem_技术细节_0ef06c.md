---
name: 技术细节, Pydantic, 代码重构
description: 在 src/core/models.py 中，将 Position 和 ForecastResult 等核心模型从 dataclass 迁移至 Pydantic...
type: user
userId: 1
tags: 技术细节, Pydantic, 代码重构
relations: stock_skills,Pydantic V2,src/core/models.py
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-25T01:58:17.6290394Z
lastAccessedAt: 2026-05-25T01:58:17.6290396Z
---

在 src/core/models.py 中，将 Position 和 ForecastResult 等核心模型从 dataclass 迁移至 Pydantic V2，以实现严格的类型校验。