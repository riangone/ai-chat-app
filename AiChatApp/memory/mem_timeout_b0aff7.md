---
name: timeout,aisservice,process-management,fix
description: 用户反映AI回复在完成前被中断，原因是AiService.cs中存在硬超时机制（Task.Delay + process.Kill）。修复方案：appsetti...
type: user
userId: 1
tags: timeout,aisservice,process-management,fix
relevanceScore: 80
accessCount: 3
createdAt: 2026-04-27T23:53:41.7429006Z
lastAccessedAt: 2026-04-29T05:12:09.1644394Z
---

用户反映AI回复在完成前被中断，原因是AiService.cs中存在硬超时机制（Task.Delay + process.Kill）。修复方案：appsettings.json的TimeoutSeconds从300秒提升至600秒，AiService.cs代码内默认值从90秒提升至300秒，并使用CancellationTokenSource在进程正常结束时取消超时监视任务。