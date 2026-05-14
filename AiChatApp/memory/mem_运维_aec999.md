---
name: 运维,工作流,部署
description: NetYamlForge 的标准重启流程包括：停止旧进程、执行 Release 编译 (dotnet build -c Release)、并使用 start.s...
type: user
userId: 1
tags: 运维,工作流,部署
relations: NetYamlForge,start.sh
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-13T08:56:40.5710195Z
lastAccessedAt: 2026-05-13T08:56:40.5710196Z
---

NetYamlForge 的标准重启流程包括：停止旧进程、执行 Release 编译 (dotnet build -c Release)、并使用 start.sh 脚本启动。