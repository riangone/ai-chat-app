---
name: 新增类,BizCardParserExecutor,AI OCR,IEmbeddingService
description: 新建 BizCardParserExecutor.cs (类型 biz_card_parser)，用于处理 import_jobs 任务。该执行器利用 AI O...
type: user
userId: 1
tags: 新增类,BizCardParserExecutor,AI OCR,IEmbeddingService
relations: BizCardParserExecutor,import_jobs,business_cards,IEmbeddingService
relevanceScore: 33
accessCount: 0
createdAt: 2026-06-20T12:13:34.2837659Z
lastAccessedAt: 2026-06-20T12:13:34.2837660Z
boundAgentRole: 
---

新建 BizCardParserExecutor.cs (类型 biz_card_parser)，用于处理 import_jobs 任务。该执行器利用 AI OCR 解析名刺并写入 business_cards，随后内联生成嵌入并更新任务状态。它实现了 IEmbeddingService 框架核心接口，并支持 lmstudio、gemini、antigravity、ollama、anthropic 五个 AI 提供商。