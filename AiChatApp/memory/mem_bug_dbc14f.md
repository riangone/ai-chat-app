---
name: bug,AiService,system-prompt,streaming,filter,Hyperion
description: AiService.cs中存在一个bug：AI代理在每次回复时会在开头回显系统提示词（如Hyperion的身份设定），原因是过滤片段不完整且流式响应缺乏过滤逻辑...
type: user
userId: 1
tags: bug,AiService,system-prompt,streaming,filter,Hyperion
relevanceScore: 80
accessCount: 0
createdAt: 2026-05-06T13:23:45.2316413Z
lastAccessedAt: 2026-05-06T13:23:45.2316414Z
---

AiService.cs中存在一个bug：AI代理在每次回复时会在开头回显系统提示词（如Hyperion的身份设定），原因是过滤片段不完整且流式响应缺乏过滤逻辑。修复方案：(1)完善SystemPromptFragments静态列表以精准识别系统提示词片段；(2)重构GetResponseStreamAsync方法，引入最大4096字符的前缀缓冲区，在流式输出开始前自动过滤回显的系统指令。