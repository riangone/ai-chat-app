---
name: mem_bug_fix_system_prompt_leak
description: # Bug Fix: System Prompt Leak in Responses

## Issue
AI responses frequently inc...
type: user
userId: 0
tags: mem_bug_fix_system_prompt_leak
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 42
createdAt: 2026-05-07T23:25:23.2073626Z
lastAccessedAt: 2026-05-14T03:29:19.0798569Z
---

# Bug Fix: System Prompt Leak in Responses

## Issue
AI responses frequently included echoed system prompt content at the beginning of the message, especially when using CLI providers like `gemini` or `claude` in certain modes, or when the system prompt contained dynamic content (memories, policies) not covered by hardcoded stripping fragments.

## Fix
Improved the response cleaning logic in `AiService.cs`:
1.  **Dynamic Stripping:** `CleanResponse` and `StripEchoedPromptPrefix` now accept the actual `systemPrompt` and `userPrompt` sent to the AI.
2.  **Line-by-Line Validation:** The stripper now builds a dynamic set of fragments from the current prompt lines and uses them to identify and skip echoed lines in the response.
3.  **Enhanced Robustness:**
    - Increased line limit from 150 to 300 lines for echo detection.
    - Increased streaming prefix buffer from 4KB to 16KB.
    - Added logic to handle wrapped lines by checking if a prompt line contains the output line.
    - Expanded hardcoded `SystemPromptFragments` with more common patterns.

## Files Modified
- `AiChatApp/Services/AiService.cs`

## Verification
- Project builds successfully.
- Logic covers both synchronous (`ExecuteCliAsync`) and streaming (`GetResponseStreamAsync`) paths.