---
name: architecture, NetYamlForge, design-pattern
description: NetYamlForge 项目采用“子项目独立化”架构，核心原则是将特定功能的控制器、模型和视图完整迁移到 `projects/{project_name}/`...
type: user
userId: 1
tags: architecture, NetYamlForge, design-pattern
relations: NetYamlForge,userhome
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-14T02:27:09.6683632Z
lastAccessedAt: 2026-05-14T02:27:09.6683633Z
---

NetYamlForge 项目采用“子项目独立化”架构，核心原则是将特定功能的控制器、模型和视图完整迁移到 `projects/{project_name}/` 目录中，实现逻辑独立。