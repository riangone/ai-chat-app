---
name: bug_fix,frontend,cshtml,polling
description: 已整合关于 bug_fix 的内存记录。主要修复内容如下：

1. **BatchJob 轮询与状态控制修复**：
   - 修改了 `_JobRow.csht...
type: user
userId: 1
tags: bug_fix,frontend,cshtml,polling
relations: _JobRow.cshtml,BatchJobController.cs,_List.cshtml,project_settings,Validator,Gemini CLI,AnnotationPrompt
relevanceScore: 90
accessCount: 68
createdAt: 2026-06-20T02:04:22.8621270Z
lastAccessedAt: 2026-06-23T14:45:49.0867013Z
boundAgentRole: 
---

已整合关于 bug_fix 的内存记录。主要修复内容如下：

1. **BatchJob 轮询与状态控制修复**：
   - 修改了 `_JobRow.cshtml`，使其轮询 URL 携带 `since` 参数以传递 `triggerTime`。
   - 修复了 `BatchJobController.cs` 中的 `TriggerJob` 方法，强制设置 `IsRunning = true` 并记录 `TriggerTime`；同时为 `JobStatus` 新增 `since` 参数，若最新执行时间 `LastExecution.ExecutedAt < since` 则保持 `IsRunning = true`（限时 10 分钟），以解决状态同步滞后问题。

2. **标注动作性能与界面修复**：
   - 解决项目中 `annotate_now` 动作同步阻塞 60-90 秒的问题，通过清理 `project_settings` 中重复的 `annotation_provider` 配置行得以解决。
   - 修复了 `_List.cshtml` 因未过滤 scope 导致操作按钮渲染混乱以及样式未正确应用的问题。

3. **Validator 改进（包含在修复的 45 个问题中）**：
   - 使种子检查兼容驼峰命名表名（例如使 `filter_demo` 兼容 `filterdemo`）。
   - 将占位符检查修改为大小写精确且与单词边界匹配，防止类似 "Todo App" 的名称被误判为 "TODO" 占位符。

4. **Gemini CLI 标注修复与优化**：
   - 解决 Gemini CLI 未正确读取图片内容导致标注完全错误的问题，将命令行参数格式修改为 `@filepath` 语法（即 `gemini -p "@/path/to/photo.jpg\n{prompt}"`）。
   - 解决标注输出为英文的问题，将 `AnnotationPrompt` 更改为中文 Prompt，确保生成中文描述和标签。