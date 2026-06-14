---
name: code_modification,AiPromptService.cs,system_prompt,recency_effect
description: 在服务类 [AiPromptService](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/AiP...
type: user
userId: 1
tags: code_modification,AiPromptService.cs,system_prompt,recency_effect
relations: AiPromptService,BuildSystemPromptAsync,SystemPrompt,LongTermMemory,Hyperion
relevanceScore: 90
accessCount: 0
createdAt: 2026-06-14T03:15:38.0079046Z
lastAccessedAt: 2026-06-14T03:15:59.1357464Z
boundAgentRole: 
---

在服务类 [AiPromptService](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/AiPromptService.cs) 的系统提示词构建方法 [BuildSystemPromptAsync](file:///home/ubuntu/ws/ai-chat-app/AiChatApp/Services/AiPromptService.cs#L109-L175) 中，为了改进模型提示词质量，进行了两处关于提示词结构的修改：
1. **引入记忆引导句**：在长期记忆数据注入前，添加了专门的引导文本（“以下はこのユーザーとのこれまでの会話から得られた情報です。応答内容や対応の仕方をこれに合わせて調整してください。”），以指导模型利用长期记忆来个性化其互动风格。
2. **强化近因效应设定**：在系统提示词的末尾（即可用工具动作之后）追加了简短的 `[REMINDER]` 块（“Above all, stay in character as Hyperion: concise, direct, and aligned with the tone/style guidance at the top of this prompt.”），用于强化模型在回复时扮演 Hyperion 角色时的简洁和直接设定。