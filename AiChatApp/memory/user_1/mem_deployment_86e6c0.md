---
name: deployment,restart,process
description: 应用当前采用 Caddy 反向代理至 5001 端口的 Release DLL 运行。在此前的部署中，曾因 5021 端口的旧 `dotnet run` 进程未...
type: user
userId: 1
tags: deployment,restart,process
relations: Caddy,Release DLL,5001端口,5021端口,dotnet run,_SectionForm.cshtml,PID 3378408,PID 3376899
relevanceScore: 72
accessCount: 0
createdAt: 2026-06-21T06:14:20.6178494Z
lastAccessedAt: 2026-06-21T06:14:33.2798762Z
boundAgentRole: 
---

应用当前采用 Caddy 反向代理至 5001 端口的 Release DLL 运行。在此前的部署中，曾因 5021 端口的旧 `dotnet run` 进程未及时停止，导致新修改的 `_SectionForm.cshtml` 模板渲染无法生效。目前，该冲突已通过清理旧进程得以解决；应用已成功加载新 DLL 重启，当前新进程 PID 为 3378408，已完全替代 PID 为 3376899 的旧进程。