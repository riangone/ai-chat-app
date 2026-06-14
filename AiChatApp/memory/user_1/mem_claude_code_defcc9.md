---
name: Claude Code,Cloud Assistant,Deployment,Architecture
description: ### 1. 部署与配置方案
- **本地助手（方案 A）**：是最快上手的方式。核心是通过编写项目根目录下的 `CLAUDE.md`（配置语言偏好、工作习惯和...
type: user
userId: 1
tags: Claude Code,Cloud Assistant,Deployment,Architecture
relations: Claude Code,CLAUDE.md,Claude Fable 5,Hyperion,VPS,Tailscale,Discord bot
relevanceScore: 90
accessCount: 0
createdAt: 2026-06-14T01:47:48.8662939Z
lastAccessedAt: 2026-06-14T01:48:04.1226777Z
boundAgentRole: 
---

### 1. 部署与配置方案
- **本地助手（方案 A）**：是最快上手的方式。核心是通过编写项目根目录下的 `CLAUDE.md`（配置语言偏好、工作习惯和回复风格）以及在 `.claude/commands/` 目录下创建自定义斜杠命令来实现。
- **24/7 云端助手（方案 B）**：虽然可实现全天候和移动端访问，但需要依赖 VPS、tmux、Tailscale 和 Discord bot。其运维（如保活、错误处理、安全配置）复杂度和维护成本较高。

### 2. 记忆系统
- Claude Code 实际上具备跨会话的记忆系统（数据存储于本地目录如 `~/.claude/projects/.../memory/`），每次新会话并非完全从空白开始，因此不完全依赖 `CLAUDE.md` 来模拟持久记忆。

### 3. 模型支持与命令
- **支持模型**：Claude Code 支持多种模型，包括 Claude Fable 5（模型 ID：`claude-fable-5`，拥有 1M 上下文窗口，定位高于 Claude Opus）、Claude Opus 4.8、Claude Sonnet 4.6 和 Claude Haiku 4.5。
- **切换命令**：
  - 用户可以使用 `/model` 命令查看或切换当前模型。
  - 使用 `/fast` 命令可以切换到 Fast mode（当前会话所使用的模型是 Claude Fable 5）。

### 4. 安全与角色限制
- Claude Code 曾拒绝 “Hyperion” 的角色设定。这是因为其安全对齐（RLHF）和高优先级系统指令会将任何修改 AI 根本身份的行为判定为身份劫持与提示词注入。