---
name: user_preference, language, communication
description: 用户偏好与开发规范整合如下：
1. 助手定位与语言偏好：将助手定位为个人助理。在此后的所有回复（包括项目进度汇报）中必须全部使用中文进行交流。系统提示词模板需支...
type: user
userId: 1
tags: user_preference, language, communication
relations: User,Assistant,AiChatApp,NetYamlForge,codex CLI,ai-doc-processor
relevanceScore: 90
accessCount: 0
createdAt: 2026-06-14T10:40:31.8109125Z
lastAccessedAt: 2026-06-14T10:40:58.3889027Z
boundAgentRole: 
---

用户偏好与开发规范整合如下：
1. 助手定位与语言偏好：将助手定位为个人助理。在此后的所有回复（包括项目进度汇报）中必须全部使用中文进行交流。系统提示词模板需支持中文和日语双语版本，以便在不同语言的 AI 代理中准确运行。
2. 开发流程偏好：遵循先编写详细设计文档，然后再委托子代理进行具体功能实现的开发流程。
3. 子代理与 AI 进化偏好：子代理不使用 Claude 自有的子代理，而是调用其他的 AI CLI（例如 codex CLI）来执行任务。同时，需设计一套方案，通过充分利用多个可用的 AI CLI 来实现 AI 的自我进化。
4. 数据格式偏好：AI 在查询日本企业信息后，必须以严格的 JSON 格式返回结果。
5. 界面展示偏好：系统首页应展示子项目的实际功能，而非使用通用模板。
6. 代码与文档提交、恢复与排除规则：
   - 在处理代码提交时，必须恢复被误删或标记为删除的 'AiChatApp/memory/' 目录下的长期记忆文件，不予提交删除。
   - 在提交和推送代码与文档时，必须排除 ai-doc-processor 项目下的上传文件目录（NetYamlForge/wwwroot/uploads/ai-doc-processor/）以及 SQLite 数据库文件（NetYamlForge/projects/ai-doc-processor/database/ai-doc-processor.db）。