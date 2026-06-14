---
name: issue,code-quality,nullable
description: - **Nullable 检查失效问题**：NetYamlForge 的 csproj 文件中使用了 NoWarn 压制了空引用警告（CS8602/CS8620...
type: user
userId: 1
tags: issue,code-quality,nullable
relations: NetYamlForge,csproj,Git 仓库,Nullable 检查,.gitignore
relevanceScore: 24
accessCount: 0
createdAt: 2026-06-11T22:30:54.2547424Z
lastAccessedAt: 2026-06-11T22:31:07.3027486Z
boundAgentRole: 
---

- **Nullable 检查失效问题**：NetYamlForge 的 csproj 文件中使用了 NoWarn 压制了空引用警告（CS8602/CS8620/CS8625），导致 Nullable 检查形同虚设，建议后续分模块逐步清理解决。
- **Git 仓库文件错误跟踪问题**：NetYamlForge 项目的 Git 仓库目前错误地跟踪了 runtime/数据库生成物（如 projects/*/database/*.db、system.db-wal）、日志（logs/）、缓存（cache/）以及进程文件（netyamlforge.pid），建议通过 .gitignore 忽略并使用 seed 脚本重建演示数据。