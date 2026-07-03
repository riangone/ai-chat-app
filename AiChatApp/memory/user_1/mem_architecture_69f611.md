---
name: architecture,pipeline,pulse,ledger,worktree
description: ## Loop Engineering Pattern
The Loop Engineering pattern consists of 6 component...
type: user
userId: 1
tags: architecture,pipeline,pulse,ledger,worktree
relations: Pulse,Ledger,Worktree,CooperateAsync,Eval,Commit,Automations,Skills (SKILL.md),Connectors/Plugins (MCP),Sub-agents,State Ledger,Verification Debt,Understanding Debt,Thinking Debt,NetYamlForge,YamlHotReloadService,AutomatedBlogGeneratorExecutor
relevanceScore: 90
accessCount: 1
createdAt: 2026-07-03T04:56:05.9383174Z
lastAccessedAt: 2026-07-03T05:06:21.9747529Z
boundAgentRole: 
---

## Loop Engineering Pattern
The Loop Engineering pattern consists of 6 components: Automations, Worktrees, Skills (SKILL.md), Connectors/Plugins (MCP), Sub-agents, and State Ledger. The system executes a concurrent pipeline: Pulse → Ledger → Worktree → CooperateAsync(opencode) → Eval → Commit. Practitioners must guard against three warnings: (1) Verification Debt — shifting verification responsibilities onto the loop rather than ensuring outputs are correct upfront; (2) Understanding Debt — prioritizing speed over genuine understanding of the system; (3) Thinking Debt — using loops as an excuse to avoid deliberate reasoning.

## NetYamlForge Architecture
NetYamlForge currently supports AI, BatchJob, and Connection management, but is undergoing design improvements to address deficiencies in foundational capabilities: authentication/authorization, form validation, audit logging, event dispatch, and multi-tenancy. Key implementation details: (1) Hot-reload logic is handled by YamlHotReloadService.cs; (2) The daily scheduled news distribution uses blog_generator.yml as its configuration file and AutomatedBlogGeneratorExecutor.cs as its executor, which fetches RSS feeds and injects them into prompts to auto-generate news briefings.