# 资源浪费 & Token 消耗修复清单

文档日期：2026-05-17  
分析范围：`AiChatApp/Services/`、`Endpoints/`

---

## 问题总览

| # | 严重度 | 类型 | 问题 | 文件 |
|---|--------|------|------|------|
| 1 | P0 | Bug | `MemoryConsolidationService` 全局计数器跨用户共享，采样行为混乱 | `MemoryConsolidationService.cs:20` |
| 2 | P0 | Token 浪费 | `ProcessWelcomeInsightAsync` 双重 AI 调用（Summarizer + HyperionBrain） | `ProactiveBrainService.cs:67,70` |
| 3 | P1 | 性能 | `MemoryGraphService` 注册为 Scoped，每请求重建图，1 分钟缓存从未生效 | `ServiceExtensions.cs` |
| 4 | P1 | 性能 | `SearchAsync` 加载 15 条 + 图扩展（最多 45 条），最终只用 5 条 | `MemorySearchService.cs:24` |
| 5 | P1 | DB 浪费 | `SearchSkillsAsync` 全量加载所有技能后在内存过滤关键词 | `MemorySearchService.cs:50` |
| 6 | P1 | 重复 IO | `GetAvailableAgentsAsync` 在同一流程中调用两次 | `AiService.cs:1505,1546` |
| 7 | P2 | DB 浪费 | `CooperateAsync` 中同一 `AgentStep` 实体被多次单独保存 | `AiService.cs:314,324,343,351` |
| 8 | P2 | Token 浪费 | 记忆整合发送完整对话原文给 AI，无长度截断 | `MemoryConsolidationService.cs:31` |

---

## 详细说明与修复方案

### 问题 1 — MemoryConsolidationService 全局计数器跨用户共享（P0）

**根因**：`_consolidationCounter` 是 `static int`，所有用户共享同一计数器。10 个不同用户各发 1 条消息，全局计数器递增到 10，触发记忆整合——但触发的是**最后那个用户**的整合，其他 9 人的对话永远不会被整合。采样设计的意图（每个用户每 10 次触发一次）完全失效，变成随机抽某用户执行。

**修复**：改为 per-user 限流计时器，每个用户独立的最小间隔控制。

```csharp
// Before
private static int _consolidationCounter;
private const int ConsolidationSampleRate = 10;
// ...
Interlocked.Increment(ref _consolidationCounter);
if (_consolidationCounter % ConsolidationSampleRate != 0) return;

// After
private static readonly ConcurrentDictionary<int, DateTime> _lastConsolidationTime = new();
private static readonly TimeSpan ConsolidationMinInterval = TimeSpan.FromMinutes(5);
// ...
if (_lastConsolidationTime.TryGetValue(userId, out var last) &&
    DateTime.UtcNow - last < ConsolidationMinInterval) return;
_lastConsolidationTime[userId] = DateTime.UtcNow;
```

---

### 问题 2 — ProcessWelcomeInsightAsync 双重 AI 调用（P0）

**根因**：`ProcessWelcomeInsightAsync` 中串行调用 2 次 AI：  
1. `Summarizer` — 生成项目状态总结  
2. `HyperionBrain` — 根据总结生成欢迎建议  
两次调用共同产生约 2000–5000 token 消耗，且每次用户连接均触发（30 分钟冷却已有，但双倍成本没有必要）。

**修复**：合并为一次调用，让 HyperionBrain 直接根据原始输入生成建议。

```csharp
// Before: 两次串行 AI 调用
var summary = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.Summarizer, summaryInput, userId, provider: "opencode");
var insight = await aiService.ExecuteProactiveAgentAsync(ProactiveAgentProfiles.HyperionBrain, $"请根据以下总结...: {summary}", userId, provider: "opencode");

// After: 单次调用
var insight = await aiService.ExecuteProactiveAgentAsync(
    ProactiveAgentProfiles.HyperionBrain,
    "请快速分析当前项目状态并给出一句精炼的欢迎建议（30字以内，使用 Markdown 加粗重点）。",
    userId, provider: "opencode");
```

---

### 问题 3 — MemoryGraphService Scoped 注册导致缓存无效（P1）

**根因**：`MemoryGraphService` 有 1 分钟的 `_lastBuildTime` 图缓存，但注册为 `Scoped`（每请求新实例），`_lastBuildTime` 始终是 `DateTime.MinValue`，图缓存从未命中，每请求重新扫描文件系统重建图。

**修复**：改为 `Singleton`（`_adjList` 已是 `ConcurrentDictionary`，天然线程安全）。

```csharp
// ServiceExtensions.cs - Before
services.AddScoped<MemoryGraphService>();

// After
services.AddSingleton<MemoryGraphService>();
```

---

### 问题 4 — SearchAsync 图扩展过度加载（P1）

**根因**：`SearchAsync` 固定加载 15 条初始结果，再对每条做图扩展（每条 2 条关联），最多产生 `15 + 30 = 45` 条候选，最终 `.Take(5)` 只保留 5 条。80%–90% 的 IO 和内存开销被丢弃。

**修复**：减少初始加载数量，仅在结果不足时做图扩展。

```csharp
// Before: 固定加载 15 条
var initialResults = await _fileService.SearchAsync(prompt, userId, 15);

// After: 先加载目标数量，不足时才扩展
var initialResults = await _fileService.SearchAsync(prompt, userId, maxResults);
if (initialResults.Count >= maxResults)
    return initialResults;

// 结果不足时才进行图扩展
_graphService.BuildGraph(userId);
// ... 图扩展逻辑
return expandedResults.Take(maxResults).ToList();
```

---

### 问题 5 — SearchSkillsAsync 全量加载内存过滤（P1）

**根因**：将该用户所有启用的技能全部拉出来（`ToListAsync()`），再在应用层过滤 `TriggerKeywords`。技能数量增长后每次搜索成本线性增加。

**修复**：`TriggerKeywords` 为空的技能（全局技能）直接 DB 查出；有关键词的技能将关键词存于独立列或改用全文搜索。当前规模下可接受，记录为技术债。

---

### 问题 6 — GetAvailableAgentsAsync 重复调用（P1）

**根因**：`ExecuteTaskGraphAsync`（line 1505）和 `ReviseFailedSubtasksAsync`（line 1546）各自独立调用 `GetAvailableAgentsAsync`，后者在需要修订时执行，即同一次协作流程中调用两次。

**修复**：将 `allAgents` 作为参数传入 `ReviseFailedSubtasksAsync`，复用已加载结果。

```csharp
// Before: ReviseFailedSubtasksAsync 内部重复加载
var allAgents = await GetAvailableAgentsAsync(userId);

// After: 由调用方传入
private async Task<bool> ReviseFailedSubtasksAsync(
    ..., List<AgentDefinition> allAgents, ...)
{
    // 直接使用 allAgents，不再重新加载
}
```

---

### 问题 7 — CooperateAsync 中 SaveChangesAsync 密集调用（P2）

**根因**：`CooperateAsync` 的 Pipeline 模式中，同一个 `stageStep` 对象在一次 `for` 循环迭代中可能被保存 3 次：  
- `toolOutput != step.Output` 时保存（line 314）  
- Schema 验证失败时设 `WasAccepted = false` 保存（line 324）  
- 质量检查失败时设 `WasAccepted = false` 保存（line 343）  
- 最终成功设 `WasAccepted = true` 保存（line 351）  

**修复**：仅在循环结束（`break` 前）或确实需要持久化中间状态时保存，失败重试路径改为 `continue` 前不保存。

---

### 问题 8 — 记忆整合发送完整对话无截断（P2）

**根因**：`MemoryConsolidationService` 构造提取 prompt 时直接拼接 `userMessage` + `aiResponse` 全文，无长度限制。长对话时可能发送 5000+ token 的内容给 AI 用于提取记忆，而实际有价值的信息通常在前 1000 字内。

**修复**：对 `userMessage` 和 `aiResponse` 分别截断再拼接。

```csharp
// Before
User: {userMessage}
Assistant: {aiResponse}

// After
private const int MaxConsolidationLength = 800;
User: {Truncate(userMessage, MaxConsolidationLength)}
Assistant: {Truncate(aiResponse, MaxConsolidationLength)}
```

---

## 预期收益

| 修复 | Token 节省 | 性能提升 |
|------|-----------|---------|
| #1 记忆整合采样修复 | 恢复预期的 per-user 触发（由混乱变为正确） | — |
| #2 欢迎洞察 2→1 次 AI 调用 | 每次连接节省 ~50% token（约 1000–2500 token） | — |
| #3 MemoryGraph 改 Singleton | — | 消除每请求文件系统重扫，图查询性能提升显著 |
| #4 SearchAsync 减少加载 | — | 减少 70% 文件 I/O 和内存分配 |
| #5 SearchSkillsAsync（记录为债） | — | 当前规模可接受，后续优化 |
| #6 GetAvailableAgentsAsync 去重 | — | 修订路径减少 1 次文件系统扫描 |
| #7 SaveChangesAsync 合并 | — | Pipeline 每步骤减少 1–2 次 DB 往返 |
| #8 记忆整合截断 | 每次整合节省最多 ~4000 token | — |
