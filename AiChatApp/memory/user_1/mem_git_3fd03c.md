---
name: git,memory,cleanup
description: 1. 仓库清理与配置：已停止跟踪 19 个本地数据库 `.db` 运行时文件并扩展了 `.gitignore`，根目录下的临时脚本已归档至 `scripts/`...
type: user
userId: 1
tags: git,memory,cleanup
relations: git,nyf,feature/hyperion-improvements,memory,scripts,ai-doc-processor,database
relevanceScore: 90
accessCount: 0
createdAt: 2026-06-14T09:58:47.6799138Z
lastAccessedAt: 2026-06-14T09:59:01.9826449Z
boundAgentRole: 
---

1. 仓库清理与配置：已停止跟踪 19 个本地数据库 `.db` 运行时文件并扩展了 `.gitignore`，根目录下的临时脚本已归档至 `scripts/` 目录。注意，在提交和推送时必须排除 `ai-doc-processor` 目录下的上传文件和数据库文件。另外，工作区的 `memory/` 目录下有大量文件被标记为删除，可能是之前的遗留操作，需要用户确认是否恢复。
2. 分支状态与用户偏好：用户倾向于将代码和文档推送至 `nyf` 分支。项目当前处于 `feature/hyperion-improvements` 分支，且工作区有未提交的修改。
3. 分支开发动态：
   - `nyf` 分支：开发代码已按改动真实归属切分为 7 个提交（包含提交 `ac5f4bc`），并成功推送至远程 `origin/nyf` 分支，且 653/653 项 `dotnet test` 测试已全部通过。
   - 其他分支：集成了 Headroom AI 极其多 AI 评议改进后的代码已推送至远程 `origin/feature/hyperion-improvements` 分支。