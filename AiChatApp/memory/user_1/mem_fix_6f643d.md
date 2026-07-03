---
name: fix,WorktreeManagerService,git,worktree
description: Three bugs were fixed:
- Bug 1: In Task.WhenAll, each concurrent branch now crea...
type: user
userId: 1
tags: fix,WorktreeManagerService,git,worktree
relations: RemoveAsync,repoRootPath,Task.WhenAll,IServiceProvider,AppDbContext,GitCommitPulseSource,PulseActionService
relevanceScore: 90
accessCount: 0
createdAt: 2026-07-03T04:56:05.9363425Z
lastAccessedAt: 2026-07-03T04:56:30.0772641Z
boundAgentRole: 
---

Three bugs were fixed:
- Bug 1: In Task.WhenAll, each concurrent branch now creates its own IServiceProvider scope, yielding independent AppDbContext, GitCommitPulseSource, and PulseActionService instances per branch, avoiding shared-state conflicts.
- Bug 2/3: RemoveAsync was updated to accept a repoRootPath parameter, ensuring git commands execute in the correct repository directory rather than the process working directory.