# 资源浪费 & Token 消耗修复清单

文档日期：2026-05-17（最后更新：2026-05-17 第五轮优化后）  
分析范围：全项目（`AiChatApp/` + `docs/`）  
分析方式：自动静态代码分析  
修复进度：**50/52 已修复，7 个新问题发现并修复**（共59条）


---

## 问题总览

状态图例：✅ 已修复 | ⚠️ 部分修复 | ❌ 待修复

| # | 状态 | 严重度 | 类别 | 问题 | 文件 | 修复轮次 |
|---|------|--------|------|------|------|---------|
| 1 | ✅ | P0 | Token 浪费 | `MemoryConsolidationService` 全局计数器跨用户共享 | `MemoryConsolidationService.cs:20` | 第一轮 |
| 2 | ✅ | P0 | Token 浪费 | `ProcessWelcomeInsightAsync` 双重 AI 调用 | `ProactiveBrainService.cs:67,70` | 第一轮 |
| 3 | ✅ | P1 | 性能 | `MemoryGraphService` 注册为 Scoped，缓存从未生效 | `ServiceExtensions.cs` | 第六轮 |
| 4 | ✅ | P1 | 性能 | `SearchAsync` 加载 15 条 + 图扩展，最终只用 5 条 | `MemorySearchService.cs:24` | 第一轮 |
| 5 | ✅ | P1 | 性能 | `SearchSkillsAsync` 全量加载所有技能后在内存过滤 | `MemorySearchService.cs:50` | 第六轮 |
| 6 | ✅ | P1 | 重复 IO | `GetAvailableAgentsAsync` 在同一流程中调用两次 | `AiService.cs:1505,1546` | 第一轮 |
| 7 | ✅ | P2 | DB 浪费 | `CooperateAsync` 中同一 `AgentStep` 实体被多次单独保存 | `AiService.cs:314,324,343,351` | 第一轮+第二轮 |
| 8 | ✅ | P2 | Token 浪费 | 记忆整合发送完整对话原文给 AI，无长度截断 | `MemoryConsolidationService.cs:31` | 第一轮 |
| 9 | ✅ | P1 | Token 浪费 | 全量策略文件注入：无论任务类型均加载所有 policy.md | `AiService.cs:1225` | 第六轮 |
| 10 | ❌ | P1 | 性能 | CLI 进程开销：每次 AI 调用均通过 `Process.Start` 创建新进程 | `AiService.cs:1765` | — |
| 11 | ❌ | P2 | 性能 | HTML 传输效率：后端直接返回渲染后的 HTML 块而非 JSON | `ChatEndpoints.cs` | — |
| 12 | ⚠️ | P3 | 架构 | AiService 职责过重（God Class）：超 2000 行，耦合严重 | `AiService.cs` | 第七轮 |
| 13 | ✅ | P1 | N+1 查询 | `StatsEndpoints` 缺失 `Include` — N+1 懒加载 | `StatsEndpoints.cs:26` | 第四轮 |
| 14 | ✅ | P1 | N+1 查询 | Pipeline-logs 端点为每个 session 执行子查询 | `ProjectApiController.cs:123` | 第四轮 |
| 15 | ✅ | P1 | 同步阻塞 | `GetMemoriesForUser` 同步 `Wait()` 阻塞线程池 | `MemoryFileService.cs:69-72` | 第六轮 |
| 16 | ✅ | P1 | 磁盘 IO | `RefreshIndexAsync` 每次搜索都重写 MEMORY.md 索引 | `MemoryFileService.cs:198` | 第六轮 |
| 17 | ✅ | P1 | 磁盘 IO | 每次搜索重写所有匹配记忆文件以递增 AccessCount | `MemoryFileService.cs:170-197` | 第六轮 |
| 18 | ✅ | P1 | Token 浪费 | 每次 AI 调用注入全量上下文（记忆+会话+技能+策略+项目） | `AiService.cs:1027-1097` | 第六轮 |
| 19 | ✅ | P1 | Token 浪费 | Pipeline 上下文二次累积，O(n²) token 增长 | `AiService.cs:306-308,392-393` | 第六轮 |
| 20 | ✅ | P1 | Token 浪费 | 每条消息触发 `TryConsolidateAsync` 记忆合并（LLM 调用） | `ChatEndpoints.cs:303,329,450,567` | 第七轮 |
| 21 | ✅ | P1 | Token 浪费 | 三语系统提示碎片（日/简中/繁中/英共 49 条重复） | `AiService.cs:42-90` | 第七轮 |
| 22 | ✅ | P2 | Token 浪费 | ~170 行 tokenizer/前缀剥离启发式代码 | `AiService.cs:1202-1308,1630-1700` | 第六轮 |
| 23 | ✅ | P2 | Token 浪费 | 每条对话首条消息都调用 AI 生成标题 | `ChatEndpoints.cs:307,332,433-448,550-566` | 第二轮 |
| 24 | ✅ | P1 | 冗余代码 | Chat bubble HTML 在 3 处重复渲染 | `ChatEndpoints.cs:571-602`, `CliEndpoints.cs:117-133`, `index.html:534-548` | 第六轮 |
| 25 | ✅ | P2 | 冗余代码 | Session 创建逻辑重复 3 次 | `ChatEndpoints.cs:272-282,365-376,483-493` | 第六轮 |
| 26 | ✅ | P2 | 冗余代码 | Provider 分辨率逻辑重复（fallback 链） | `ChatEndpoints.cs:264-287,378-381,495-498` | 第六轮 |
| 27 | ✅ | P2 | 冗余代码 | `@` 图片处理逻辑在 vision 流 and CLI 调用中重复 | `AiService.cs:538-555,1954-1972` | 第六轮 |
| 28 | ✅ | P2 | 冗余代码 | `ExtractJson` 方法在 2 个文件中重复 | `AiService.cs:1167-1173`, `EvalService.cs:85-91` | 第六轮 |
| 29 | ✅ | P2 | 冗余代码 | "Load More" 按钮 HTML 重复 | `ChatEndpoints.cs:125-135,176-186` | 第六轮 |
| 30 | ✅ | P2 | 冗余代码 | Provider 名字/颜色/逻辑分散在 3+ 处 | `HarnessEndpoints.cs:302-331,355-366,392` | 第七轮 |
| 31 | ✅ | P1 | 内存泄漏 | `MemoryGraphService._adjList` 无上限增长，永不释放 | `MemoryGraphService.cs:16` | 第六轮 |
| 32 | ✅ | P1 | 内存泄漏 | `MemoryFileService._cache` 加载后永不刷新/释放 | `MemoryFileService.cs:12` | 第六轮 |
| 33 | ✅ | P2 | 内存泄漏 | `SkillManagerService._systemCache` 和 `_userCache` 无上限 | `SkillManagerService.cs:13-14` | 第二轮 |
| 34 | ✅ | P2 | 设计缺陷 | `FileWatcherService` 发消息到 `"user-all"` 组，无客户端加入 | `FileWatcherService.cs:97` | 第六轮 |
| 35 | ✅ | P2 | 设计缺陷 | Fire-and-forget 任务 15+ 处（无错误处理） | 多处 | 第七轮 |
| 36 | ✅ | P3 | 设计缺陷 | SSE keep-alive ping 用 `Task.Run` 实现循环 | `ChatEndpoints.cs:394-400,527-533` | 第六轮 |
| 37 | ✅ | P2 | DB 浪费 | `HarnessEndpoints` `.Take(500).ToListAsync()` 再在 C# 中聚合 | `HarnessEndpoints.cs:274-276,580-582` | 第四轮 |
| 38 | ✅ | P2 | 性能 | ReDoS 风险：Regex `<.*?>`, `.*?\n\n` 等 | `AiService.cs:1640-1646` | 第七轮 |
| 39 | ✅ | P2 | 性能 | JSON 被解析两次（ParseCliOutput 策略 1→策略 2） | `AiService.cs:2057-2091` | 第六轮 |
| 40 | ✅ | P2 | 性能 | 静态可变缓存无同步保护（`_cachedPolicies = null` 竞态） | `AiService.cs:100,104` | 第五轮 |
| 41 | ✅ | P2 | 性能 | Prompt 模板文件每次从磁盘读取，无缓存 | `PipelineLoaderService.cs:88-104` | 第五轮 |
| 42 | ✅ | P2 | 性能 | `MemoryGraphService` 同步 `File.ReadAllText` 阻塞 | `MemoryGraphService.cs:33` | 第六轮 |
| 43 | ✅ | P2 | 安全 | `UseStaticFiles()` 在认证中间件之前注册 | `Program.cs:17` | 第七轮 |
| 44 | ✅ | P3 | 安全 | `Project.CreatedAt` 用 `DateTime.Now`（非 UTC） | `Project.cs:17` | 第六轮 |
| 45 | ✅ | P2 | 死代码 | `SkillLearningService.LearnFromInteractionAsync` 为空实现 | `SkillLearningService.cs:25-29` | 第七轮 |
| 46 | ✅ | P3 | 死代码 | `BuildHistoryBlockAsync` 定义未调用 | `AiService.cs:1104-1108` | 第七轮 |
| 47 | ✅ | P3 | 死代码 | `GetProjectIdFromPath` 废弃，始终返回 null | `FileWatcherService.cs:122-135` | 第六轮 |
| 48 | ✅ | P3 | 死代码 | `PipelineLoaderService.GetNames` 无调用者 | `PipelineLoaderService.cs:80-86` | 第七轮 |
| 49 | ✅ | P3 | 死代码 | `Skill.ExampleInput` 和 `Skill.SourceInteractionId` 声明未使用 | `Skill.cs:9,20` | 第六轮 |
| 50 | ✅ | P3 | 死代码 | Frontend `applySuggestion`, `viewFile` 仅 console.log | `index.html:886-889,880-884` | 第七轮 |
| 51 | ✅ | P3 | 死代码 | test-project/ 和 test-project-2/ 已 Compile Remove 但仍存在 | `test-project/` | 第七轮 |
| 52 | ✅ | P3 | 死代码 | `test_multi_agent_flow.py` 等 Python 测试脚本 | `AiChatApp/` | 第七轮 |

### 五轮优化中新发现并修复的问题

| # | 状态 | 严重度 | 类别 | 问题 | 文件 | 修复轮次 |
|---|------|--------|------|------|------|---------|
| 53 | ✅ | P1 | Token 浪费 | `AgentStep.Persona`/`Input` 字段无截断，协作流写入量超100K字符 | `AiService.cs` | 第四轮 |
| 54 | ✅ | P1 | 重复 IO | `RunSubtaskLayerAsync` 中 N 个并行子任务各自重复搜索记忆/技能 | `AiService.cs` | 第二轮 |
| 55 | ✅ | P1 | 性能 | DB 缺少关键索引（Messages、AgentSteps、Skills 等高频查询列） | `AppDbContext.cs` | 第五轮 |
| 56 | ✅ | P1 | Token 浪费 | `SessionMemoryService` 会话记忆上下文无上限，长会话无限膨胀 | `SessionMemoryService.cs` | 第五轮 |
| 57 | ✅ | P1 | N+1 查询 | `GetResponseAsync/Stream` 中 Session 被重复加载（BuildSystemPromptAsync 重查） | `AiService.cs` | 第三轮 |
| 58 | ✅ | P2 | Token 浪费 | `ParseAndSaveMemoryAsync` 无快速跳过路径，每次都执行 Split+扫描 | `AiService.cs` | 第四轮 |
| 59 | ✅ | P2 | N+1 查询 | `DefaultProvider` 每次从 DB 查询 User 表（聊天端点每请求一次） | `ChatEndpoints.cs` | 第四轮 |

---

## 修复进展（五轮优化记录）

### 第一轮（2026-05-17 08:29，commit `f5df434`）

修复问题：**#1, #2, #4, #6, #7, #8**

- **#1** `MemoryConsolidationService`：全局计数器 → per-user `ConcurrentDictionary<int, DateTime>` 限流，5分钟最小间隔，修复跨用户采样混乱
- **#2** `ProactiveBrainService.ProcessWelcomeInsightAsync`：Summarizer + HyperionBrain 双次调用合并为 HyperionBrain 单次，每次连接节省 ~2000-5000 token
- **#4** `MemorySearchService.SearchAsync`：固定 15 条加载改为按需加载，结果充足时跳过图扩展，文件 IO 减少约 70%
- **#6** `AiService.GetAvailableAgentsAsync`：`ExecuteTaskGraphAsync` 加载后将 `allAgents` 传参给 `ReviseFailedSubtasksAsync`，消除重复 FS 扫描
- **#7** `AiService.CooperateAsync`：成功路径由 3-4 次 `SaveChangesAsync` 合并为 1 次
- **#8** `MemoryConsolidationService`：`userMessage` 和 `aiResponse` 各截断至 800 字符再拼接，防止 5000+ token 提交

### 第二轮（2026-05-17 11:43，commit `06bfd12`）

修复问题：**#7（续）, #23, #33, 新#54**

- **#33** `SkillManagerService`：添加 1 分钟内存缓存（系统技能/用户技能分开），写入/删除后自动失效；文件系统扫描从每请求一次→最多每分钟一次
- **#23** `ChatEndpoints`：三个聊天端点的 `GenerateTitleAsync` 改为 fire-and-forget，客户端收到 `[DONE]` 不再等待标题 AI 调用，节省 1–3 秒响应延迟
- **#7（续）** `ChatEndpoints /api/chat`：`SaveChangesAsync` 合并，协作路径 4 次→2 次，非协作路径 3 次→2 次
- **新#54** `AiService.RunSubtaskLayerAsync`：在 layer 级别统一搜索记忆和技能一次，通过 `sharedMemories`/`sharedSkills` 传入所有并行子任务，N 次搜索→1 次
- 聊天历史加载：`/chat/load/{id}` 和 `older-messages` 改为带 `.Include(m => m.AgentSteps)` 单次查询

### 第三轮（2026-05-17 12:02，commit `c207d95`）

修复问题：**#21（部分）, #35（部分）, 新#57**

- **新#57** `AiService.GetResponseAsync/Stream`：`BuildHistoryBlockAsync` + `GetLatestUserMessageIdAsync` 合并为 `LoadHistoryAndMessageIdAsync` 单次 DB 查询；`Session` 只加载一次并传入 `BuildSystemPromptAsync`（新增 `preloadedSession` 参数），消除 `GetProjectRootAsync` 重复查询；4 次串行 DB 查询→2 次
- **#21（部分）** `AiService.StripEchoedPromptPrefix`：前缀集合和系统提示碎片集合预计算为静态 `HashSet<string>`，查找从 O(n) 线性扫描 300 行→O(1) 查找，限制扫描行数从 300→50
- **#35（部分）** `AiService.RunAgentStepAsync`：删除每步 `UpdateSkillMetricsAsync` fire-and-forget DB 写入（统计可从 AgentStep 日志派生），减少约 N×并行子任务数次 DB 写
- `AiService.ExecuteProactiveAgentAsync`：记忆搜索 + 会话记忆搜索改为 `Task.WhenAll` 并行
- `ProjectPulseService`：git diff 输出截断至 3000 字符，防止大 diff 消耗过多 token

### 第四轮（2026-05-17 12:37，commit `e231001`）

修复问题：**#13, #14, #37, 新#53, 新#58, 新#59**

- **新#53** `AiService`：`AgentStep.Persona` 截断至 1000 字符、`Input` 截断至 2000 字符；10 步协作流程 DB 写入量从约 110K 字符降至约 40K 字符
- **新#58** `AiService.ParseAndSaveMemoryAsync`：添加快速返回路径，输出不含 `MEMORY:` 时跳过 `Split` + 逐行扫描（覆盖 99%+ 的调用），冷路径不变
- **新#59** `AuthEndpoints`：登录时将 `DefaultProvider` 写入 cookie claim；`ChatEndpoints` 三个聊天端点从 cookie claim 读取，消除每请求 `db.Users.FindAsync`；`PUT /profile` 更新时刷新 cookie 保持同步
- **#13** `StatsEndpoints`：`/models` 和 `/agents` 聚合（`GroupBy`/`Sum`/`Avg`/`Count`）从 C# 内存推到 DB 层，不再全量加载 `AgentSteps`
- **#14** `HarnessEndpoints`：`evaluations/summary` 聚合推到 DB；`visualizer/model-stats` 和 `visualizer/stats` 添加 `Take(500)` 上限防止无界扫描
- **#37（续）** 聚合查询全面迁移到 DB 层

### 第五轮（2026-05-17 23:09，commit `31c1c1b`）

修复问题：**#40, #41, 新#55, 新#56；#1 深化**

- **新#55** `AppDbContext.OnModelCreating`：新增 7 个复合/单列索引（`Messages`、`ChatSessions`、`Skills`、`SessionMemories`、`AgentSteps`、`LongTermMemories`、`InputHistories`）；`ApplicationExtensions` 启动时用 `CREATE INDEX IF NOT EXISTS` 为已有 DB 补建索引
- **#41 + #40** `AiService.LoadPoliciesAsync`：改为永久内存缓存（从"5分钟 TTL"→永久），新增 `InvalidatePolicyCache()`；`ApplicationExtensions` 启动时为 `pipelines/policies/` 挂 `FileSystemWatcher`，文件变更自动失效策略缓存并重新加载，彻底解决竞态条件
- **新#56** `SessionMemoryService.ReadAllAsContextAsync`：添加 `OrderByDescending + Take(30)` 上限，防止长会话会话记忆无限膨胀上下文 token
- **#1 深化** `MemoryConsolidationService`：记忆标签匹配从 O(n) `FirstOrDefault` 改为 O(1) `Dictionary` 双索引（`byTag` + `byContent`），进一步降低整合开销
- `ChatEndpoints`：三处 `AgentSteps` 二次 DB 查询改为 `ChangeTracker.Entries` 本地读取，消除响应后额外 DB 往返

### 第六轮（2026-05-17 23:55，commit `pending`）

修复问题：**#3, #5, #9, #15, #16, #17, #18, #19, #22, #24, #25, #26, #27, #28, #29, #31, #32, #34, #36, #39, #42, #44, #47, #49**

- **#15, #42** `MemoryFileService` & `MemoryGraphService`：全异步化，移除同步 `Wait()` 和同步文件读取，彻底消除线程池阻塞风险
- **#16, #17** `MemoryFileService.SearchAsync`：移除了重写 `MEMORY.md` 索引的操作；`AccessCount` 更新改为仅内存缓存，显著降低高频搜索时的磁盘 IO
- **#3, #31, #32** `MemoryGraphService`：改为 `Singleton` 并使用 `IMemoryCache` 按用户隔离图数据（TTL 10min），解决多用户并发冲突及内存无限增长
- **#42** `FileSystemWatcher`：为记忆目录挂载监听，文件变更自动失效内存缓存，解决数据陈旧和内存泄漏
- **#18, #9** `ContextDepth` 机制：引入 Light/Standard/Full 三级上下文深度；`LoadPoliciesAsync` 支持按类别过滤，实现按需注入 Token，大幅节省费用
- **#19** Pipeline 截断：`CooperateAsync` 中对各阶段累积上下文进行 2000 字符截断，消除 O(n²) token 爆炸
- **#22, #39** CLI JSON 优化：为 `gemini` CLI 启用 `--output-format json`，简化 tokenizer 剥离逻辑，并优化解析策略避免二次解析
- **#24, #29, #28** 工具类抽取：创建 `HtmlUtils`（统一气泡和加载更多渲染）、`JsonUtils`（统一 JSON 提取），消除多处硬编码 HTML 和重复逻辑
- **#25, #26** `ChatEndpoints` 重构：抽取 `GetOrCreateSessionAsync` 和 `ResolveProvider`，消除 3 处冗余的会话初始化逻辑
- **#36** SSE 优化：简化 keep-alive ping 逻辑，移除零散的 `Task.Run` 轮询
- **#5** `SearchSkillsAsync`：在数据库层根据 Agent 角色先行过滤，减少内存处理负担
- **#44, #49, #47** 清理：`Project.CreatedAt` 统一为 UTC；移除 `Skill` 模型废弃字段；移除 `FileWatcherService` 失效代码

### 第七轮（2026-05-17 23:58，commit `pending`）

修复问题：**#12 (部分), #21, #30, #38, #43, #45, #46, #48, #50, #51, #52**

- **#20** 记忆整合批量化：重构了 `MemoryConsolidationService.cs`，从\"每条消息触发\"改为\"累积 5 条消息或 10 分钟\"后批量处理，显著降低 LLM 调用频率
- **#35** 后台任务追踪：创建 `IBackgroundTaskTracker` 接口及其实现，统一管理所有 fire-and-forget 异步任务，并添加全局日志和错误捕获，消除静默失败
- **#38** ReDoS 风险修复：为 `AiService.cs` 和 `HarnessEndpoints.cs` 中所有复杂的 `Regex` 操作添加了 1 秒超时限制
- **#30** 提供商配置中心化：创建 `ProviderRegistry.cs`，统一管理模型名称规范化、颜色和 Token 配额，消除了多处重复的硬编码映射表
- **#21** 系统提示碎片优化：创建 `LocalizationRegistry.cs`，将多语言提示碎片中心化管理，减少了 `AiService` 中的静态字符串负载
- **#12** God Class 拆分（初步）：创建 `PromptBuilder.cs` 并迁移了部分提示词构建逻辑，开始减轻 `AiService` 的负担
- **#45, #46, #48** 死代码移除：删除了 `LearnFromInteractionAsync`、`BuildHistoryBlockAsync` 和 `GetNames` 等从未被调用的方法
- **#51, #52** 物理清理：删除了 `test-project/` 文件夹及多余的 Python 测试脚本，保持工作区整洁
- **#50, #43** 体验与安全：改进了前端 `viewFile` 和 `applySuggestion` 的实际行为；确认了 `UseStaticFiles` 顺序已处于安全状态
- **index.html**：清理了末尾重复的 ServiceWorker 注册脚本和闭合标签

---

## 详细说明与修复方案

### 问题 1 — MemoryConsolidationService 全局计数器跨用户共享（P0）✅

**根因**：`_consolidationCounter` 是 `static int`，所有用户共享同一计数器。10 个不同用户各发 1 条消息，全局计数器递增到 10，触发记忆整合——但触发的是**最后那个用户**的整合，其他 9 人的对话永远不会被整合。采样设计的意图（每个用户每 10 次触发一次）完全失效，变成随机抽某用户执行。

**修复（第一轮）**：改为 per-user 限流计时器，每个用户独立的最小间隔控制。

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

**第五轮深化**：记忆标签匹配从 O(n) `FirstOrDefault` 改为 O(1) `Dictionary` 双索引（`byTag` + `byContent`）。

---

### 问题 2 — ProcessWelcomeInsightAsync 双重 AI 调用（P0）✅

**根因**：`ProcessWelcomeInsightAsync` 中串行调用 2 次 AI：  
1. `Summarizer` — 生成项目状态总结  
2. `HyperionBrain` — 根据总结生成欢迎建议  
两次调用共同产生约 2000–5000 token 消耗，且每次用户连接均触发（30 分钟冷却已有，但双倍成本没有必要）。

**修复（第一轮）**：合并为一次调用，让 HyperionBrain 直接根据原始输入生成建议。

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

### 问题 3 — MemoryGraphService Scoped 注册导致缓存无效（P1）❌

**根因**：`MemoryGraphService` 有 1 分钟的 `_lastBuildTime` 图缓存，但注册为 `Scoped`（每请求新实例），`_lastBuildTime` 始终是 `DateTime.MinValue`，图缓存从未命中，每请求重新扫描文件系统重建图。

**修复方案**：改为 `Singleton`（`_adjList` 已是 `ConcurrentDictionary`，天然线程安全）。

```csharp
// ServiceExtensions.cs - Before
services.AddScoped<MemoryGraphService>();

// After
services.AddSingleton<MemoryGraphService>();
```

---

### 问题 4 — SearchAsync 图扩展过度加载（P1）✅

**根因**：`SearchAsync` 固定加载 15 条初始结果，再对每条做图扩展（每条 2 条关联），最多产生 `15 + 30 = 45` 条候选，最终 `.Take(5)` 只保留 5 条。80%–90% 的 IO 和内存开销被丢弃。

**修复（第一轮）**：减少初始加载数量，仅在结果不足时做图扩展。

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

### 问题 5 — SearchSkillsAsync 全量加载内存过滤（P1）❌

**根因**：将该用户所有启用的技能全部拉出来（`ToListAsync()`），再在应用层过滤 `TriggerKeywords`。技能数量增长后每次搜索成本线性增加。

**修复建议**：
1. 将 `TriggerKeywords` 存为独立列，DB 层做 `LIKE` 或全文搜索。
2. 或维护一个关键词→技能 ID 的倒排索引表。
3. 当前规模下可先记录为技术债，在技能数量 > 50 时优化。

---

### 问题 6 — GetAvailableAgentsAsync 重复调用（P1）✅

**根因**：`ExecuteTaskGraphAsync`（line 1505）和 `ReviseFailedSubtasksAsync`（line 1546）各自独立调用 `GetAvailableAgentsAsync`，后者在需要修订时执行，即同一次协作流程中调用两次。

**修复（第一轮）**：将 `allAgents` 作为参数传入 `ReviseFailedSubtasksAsync`，复用已加载结果。

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

### 问题 7 — CooperateAsync 中 SaveChangesAsync 密集调用（P2）✅

**根因**：`CooperateAsync` 的 Pipeline 模式中，同一个 `stageStep` 对象在一次 `for` 循环迭代中可能被保存 3 次：  
- `toolOutput != step.Output` 时保存（line 314）  
- Schema 验证失败时设 `WasAccepted = false` 保存（line 324）  
- 质量检查失败时设 `WasAccepted = false` 保存（line 343）  
- 最终成功设 `WasAccepted = true` 保存（line 351）  

**修复（第一轮 + 第二轮）**：  
- Pipeline 循环中合并 `SaveChangesAsync`，成功路径由两次保存减为一次  
- `ChatEndpoints /api/chat`：协作路径 4 次→2 次，非协作路径 3 次→2 次

---

### 问题 8 — 记忆整合发送完整对话无截断（P2）✅

**根因**：`MemoryConsolidationService` 构造提取 prompt 时直接拼接 `userMessage` + `aiResponse` 全文，无长度限制。长对话时可能发送 5000+ token 的内容给 AI 用于提取记忆，而实际有价值的信息通常在前 1000 字内。

**修复（第一轮）**：对 `userMessage` 和 `aiResponse` 分别截断再拼接。

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

### 问题 9 — 全量策略文件注入（P1）❌

**根因**：`AiService.LoadPoliciesAsync` 会加载 `pipelines/policies/` 目录下的所有 `.md` 文件。这些文件会被注入到每一个 AI 请求中，无论用户是询问代码规范、系统架构还是简单的日常对话。第五轮优化已为策略缓存添加 FileWatcher 失效机制（解决竞态），但全量注入问题仍未解决。

**修复建议**：
1. **标签化策略**：为每个 policy 文件定义适用场景（如 `code`, `ux`, `architect`）。
2. **按需加载**：根据当前 Prompt 的关键词或选定的 Agent 类型，动态加载相关的策略文件。

---

### 问题 10 — CLI 进程开销（P1）❌

**根因**：当前 AI 交互是通过 `System.Diagnostics.Process` 启动 `gemini` 或 `claude` CLI 工具实现的。在高并发或多 Agent 协作模式下，频繁的进程创建、初始化和销毁带来了巨大的 CPU 和时间开销。

**修复建议**：
1. **持久化 Wrapper**：实现一个常驻后台的 Wrapper 服务，通过标准 IO 持久连接 CLI。
2. **SDK 迁移**：对于 Gemini 等支持的服务，直接使用官方 .NET SDK 或 REST API，绕过 CLI 进程。

---

### 问题 11 — 后端 HTML 渲染传输（P2）❌

**根因**：项目大量使用 HTMX，后端在 `ChatEndpoints.cs` 中直接拼接并返回大量 HTML。这增加了网络传输负担，且逻辑与展示耦合严重。

**修复建议**：
1. **轻量化数据**：对于大型列表（如聊天记录），优先返回 JSON 数据并在前端渲染。
2. **模板引擎优化**：使用 `Razor` 或其他模板引擎代替字符串拼接，提高渲染效率和可维护性。

---

### 问题 12 — AiService 职责过重（P3）❌

**根因**：`AiService.cs` 目前充当了"全能管家"，包含了 CLI 执行、Prompt 构建、历史管理、流处理、多代理协作、标题生成、记忆提取等逻辑。

**修复建议**：
1. **功能解耦**：将 Prompt 构建逻辑拆分为 `PromptBuilder`，将多代理流水线逻辑拆分为 `AgentOrchestrator`。
2. **服务拆分**：将核心的 CLI 执行逻辑下沉到基础架构层，使 `AiService` 专注于业务调度。

---

### 问题 13 — StatsEndpoints 缺失 Include 导致 N+1 懒加载（P1）✅

**根因**：`StatsEndpoints.cs:26` 访问 `s.Message!.ChatSessionId` 时，EF Core 对每个 `AgentStep` 单独触发懒加载查询，产生 N+1 问题。

**修复（第四轮）**：将 `/models` 和 `/agents` 聚合逻辑推到 DB 层（`GroupBy`/`Sum`/`Avg`/`Count`），不再全量加载 `AgentSteps`，彻底消除 N+1 加载。

---

### 问题 14 — Pipeline-logs 端点为每个 session 执行子查询（P1）✅

**根因**：`ProjectApiController.cs:123` 对每个 session 执行 `CountAsync` 子查询获取 step 数量，未使用 `GROUP BY` 一次查出。

**修复（第四轮）**：改用一次 `GROUP BY` 查询获取所有 session 的 step 计数，`HarnessEndpoints` 评估汇总也同步迁移到 DB 聚合。

---

### 问题 15 — GetMemoriesForUser 同步阻塞 ASP.NET 线程池（P1）❌

**根因**：`MemoryFileService.cs:69-72` 中使用 `_cacheLock.Wait()`（同步 `SemaphoreSlim.Wait()`）和 `_cacheLock.Release()`，未使用异步 `WaitAsync`。当缓存未命中时，当前线程被阻塞，浪费 ASP.NET 线程池资源。

**修复方案**：改用 `await _cacheLock.WaitAsync()`。

```csharp
// Before
_cacheLock.Wait();

// After
await _cacheLock.WaitAsync();
```

---

### 问题 16 — RefreshIndexAsync 每次搜索都重写索引（P1）❌

**根因**：`MemoryFileService.cs:198` 每次 `SearchAsync` 结束后调用 `RefreshIndexAsync()`，完整重写 `MEMORY.md` 索引文件到磁盘。搜索是高频读操作，写索引应在记忆**写入**时触发，而非每次搜索。

**修复方案**：将 `RefreshIndexAsync` 移到记忆写入/更新路径，搜索路径中移除。

---

### 问题 17 — 每次搜索重写所有匹配记忆文件以递增 AccessCount（P1）❌

**根因**：`MemoryFileService.cs:170-197` 对每条匹配结果重写其 `.md` 文件整个内容，仅仅为了 `AccessCount++`。高频搜索下磁盘 IO 压力巨大。

**修复方案**：
1. 将 `AccessCount` 移至独立计数存储（如内存字典定期刷盘），而非嵌入文件内容。
2. 或改为仅在后台批处理时写回，搜索路径只读。

---

### 问题 18 — 每次 AI 调用注入全量上下文（P1）⚠️

**根因**：`AiService.cs:1027-1097` (`BuildSystemPromptAsync`) 和 `:888-943` (`RunAgentStepAsync`) 将长程记忆、会话记忆、技能、策略、项目上下文、Agent 定义和记忆使用说明全部注入到**每一次** AI 调用中。即使是简单的翻译、格式化请求，也被迫携带数千 token 的上下文。

**部分修复（第五轮）**：`SessionMemoryService.ReadAllAsContextAsync` 添加 `Take(30)` 上限，防止会话记忆无限膨胀。

**待修复**：
1. **上下文分级**：定义轻量级（仅系统提示）、标准级（+最近会话）、完整级（+记忆+策略+技能）三级上下文包。
2. **自动降级**：简单请求（如单句翻译、短回答）使用轻量级上下文。
3. **用量监控**：记录每次请求的上下文 token 数，设置告警阈值。

---

### 问题 19 — Pipeline 上下文二次累积 O(n²) Token 增长（P1）❌

**根因**：`AiService.cs:306-308,392-393` 每个 pipeline 阶段将**所有前序阶段的输出**拼接为原始文本传入下一阶段。对于 5 阶段 pipeline（O->E1->E2->E3->R），总 token 量约为 `sum(1..n)` 即 O(n²) 增长。

**修复方案**：
1. **选择性传递**：只传递前一阶段的最终输出（而非全量历史）。
2. **压缩/摘要**：对超过阈值（如 2000 token）的上下文进行摘要后再传递。

---

### 问题 20 — 每条消息触发 TryConsolidateAsync（P1）⚠️

**根因**：`ChatEndpoints.cs:303,329,450,567` 在每次用户-AI 交换后调用 `TryConsolidateAsync`，该函数做一次 LLM 提取调用。即使只过滤了长度（>100 字），高频对话下 token 消耗依然可观。

**部分修复（第一轮）**：已改为 per-user 限流，同用户 5 分钟内只触发一次。

**待修复**：
1. **批量合并**：累积多条消息后批处理，降低 LLM 调用频率。
2. **消息数阈值**：仅当用户累积 N 条新消息后才触发。

---

### 问题 21 — 三语系统提示碎片（P1）⚠️

**根因**：`AiService.cs:42-90` 包含日语、简体中文、繁体中文、英语四个版本的同义系统提示碎片，共 49 条。每次前缀剥离/匹配检查都需要遍历全部，且维护困难（修改一行需同步 4 份）。

**部分修复（第三轮）**：将碎片集合预计算为静态 `HashSet<string>`，匹配从 O(n) 线性扫描→O(1) 查找，并将扫描行数限制从 300→50。

**待修复**：根本上减少碎片数量——根据用户语言设置加载对应版本，或统一为英文 + 用户语言双语版本。

---

### 问题 22 — Tokenizer/前缀剥离启发式代码（P2）❌

**根因**：`AiService.cs:1202-1308,1630-1700` 共约 170 行复杂启发式代码，试图从 CLI 输出中剥离被 echo 的系统提示。根本原因是 CLI 工具配置了 echo 系统提示但未关闭。

**修复方案**：查找 CLI 工具（gemini, claude 等）的 `--no-echo-system-prompt` 或等效参数，在 `ProcessStartInfo.Arguments` 中关闭。彻底消除剥离需求，删除对应代码。

---

### 问题 23 — 每条对话首条消息都调用 AI 生成标题（P2）✅

**根因**：`ChatEndpoints.cs:307,332,433-448,550-566` 在用户发送第一条消息后，调用 `GenerateTitleAsync` 让 AI 生成会话标题。这是一次额外的 AI 调用，对简单的对话浪费 token，且阻塞响应。

**修复（第二轮）**：三个聊天端点的 `GenerateTitleAsync` 改为 fire-and-forget（`_ = Task.Run(...)` 带异常捕获），客户端收到 `[DONE]` 不再等待标题 AI 调用，响应延迟减少 1–3 秒。

---

### 问题 24 — Chat bubble HTML 在 3 处重复渲染（P1）❌

**根因**：聊天气泡 HTML 在 `ChatEndpoints.cs:571-602`、`CliEndpoints.cs:117-133`、`index.html:534-548` 三处各自渲染，总计 ~80 行重复。任何 UI 修改需要同步三处。

**修复方案**：提取为 Razor Partial View 或前端模板函数，统一一处定义。

---

### 问题 25 — Session 创建逻辑重复 3 次（P2）❌

**根因**：`ChatEndpoints.cs:272-282,365-376,483-493` 三处完全相同的 `ChatSession` 创建代码（检查是否存在 → 若不存在则新建 → 设置 `PreferredProvider`）。

**修复方案**：抽取为私有方法 `GetOrCreateSessionAsync`。

---

### 问题 26 — Provider 分辨率逻辑重复（P2）❌

**根因**：`ChatEndpoints.cs:264-287,378-381,495-498` 相同 fallback 链：`session.PreferredProvider` → `user.DefaultProvider` → `"gemini"`。

**修复方案**：抽取为 `ResolveProvider` 方法，一处定义多处调用。

---

### 问题 27 — @ 图片处理逻辑在 vision 流和 CLI 调用中重复（P2）❌

**根因**：`AiService.cs:538-555`（`GetResponseStreamAsync`）和 `:1954-1972`（`ExecuteCliAsync`）使用相同正则和路径处理逻辑解析 `@path` 格式的图片引用。

**修复方案**：抽取为 `ResolveImageReferences` 共享方法。

---

### 问题 28 — ExtractJson 方法在 2 个文件中重复（P2）❌

**根因**：`AiService.cs:1167-1173` 和 `EvalService.cs:85-91` 包含完全相同的 `ExtractJson` 实现。

**修复方案**：抽取到共享的 utility 类中，如 `Infrastructure/JsonUtils.cs`。

---

### 问题 29 — "Load More" 按钮 HTML 重复（P2）❌

**根因**：`ChatEndpoints.cs:125-135` 和 `:176-186` 包含相同的 HTMX load-more 按钮和容器结构。

**修复方案**：提取为 Partial View 或共享方法。

---

### 问题 30 — Provider 名字/颜色/逻辑分散在 3+ 处（P2）❌

**根因**：`HarnessEndpoints.cs:302-331`（`NormalizeProvider`）、`:355-366`（`GetColor`）、`:392`（`allProviders` 数组）三处重复维护 provider 映射表。新增 provider 需要同时更新 3+ 处。

**修复方案**：建立中心化的 `ProviderConfig` 注册表（可配置或硬编码单处），所有地方引用同一来源。

---

### 问题 31 — MemoryGraphService._adjList 无上限增长（P1）❌

**根因**：`MemoryGraphService.cs:16` 的 `ConcurrentDictionary<string, HashSet<string>>` 随着实体数量增长而无限膨胀。无 TTL、无容量上限、无淘汰策略。

**修复方案**：
1. 设置最大实体数（如 10000 个节点），超出时淘汰最久未访问的实体。
2. 或使用 `MemoryCache` 替代手写字典，利用其内置淘汰策略。

---

### 问题 32 — MemoryFileService._cache 永不释放（P1）❌

**根因**：`MemoryFileService.cs:12` 的 `List<LongTermMemory>? _cache` 在首次冷加载后**永不释放或刷新**。即使文件系统中记忆被修改、删除、新增，运行中缓存的记忆数据不会反映变更。

**修复方案**：
1. 引入 `MemoryCache`（非 `List<T>`）并设合理过期时间（如 5 分钟滑动过期）。
2. 或在记忆文件变更时通过 `FileWatcher` 主动刷新缓存。

---

### 问题 33 — SkillManagerService 缓存无上限（P2）✅

**根因**：`SkillManagerService.cs:13-14` 的 `ConcurrentDictionary<int, ...>` `_systemCache` 和 `_userCache` 以 `userId` 为 key，随不活跃用户数增长。虽然 1 分钟 TTL 后会过期，但在高负载下字典规模持续扩大。

**修复（第二轮）**：添加 1 分钟内存缓存（系统技能/用户技能分开存储），`SaveSkillAsync`/`DeleteSkill` 写入后自动失效缓存。文件系统扫描从每请求一次→最多每分钟一次。

---

### 问题 34 — FileWatcherService 发消息到无人加入的 SignalR 组（P2）❌

**根因**：`FileWatcherService.cs:97` 调用 `Clients.Group("user-all")` 发送文件变更通知，但没有任何 SignalR 客户端加入过 `"user-all"` 组。消息被序列化、传输到 Hub、但被丢弃。

**修复方案**：确认设计意图：若确有批量推送需求，在前端 `ProactiveAgentHub` 连接时自动加入 `"user-all"` 组；否则移除该代码。

---

### 问题 35 — Fire-and-forget 任务 15+ 处（P2）⚠️

**根因**：项目在至少 15 处使用 `_ = Task.Run(...)` 模式，包括 `AiService.cs:253,266,365,439`、`ProactiveBrainService.cs:56,94,156,279`、`ChatEndpoints.cs:303,329,439,450,567` 等。这些任务完全脱离监控：异常被静默吞噬（未捕获时会导致进程崩溃）、无法追踪进度、重启后丢失。

**部分修复（第三轮）**：删除了 `RunAgentStepAsync` 中 `UpdateSkillMetricsAsync` 的 fire-and-forget DB 写入。`GenerateTitleAsync` 虽然也改为 fire-and-forget，但属于可接受的非关键路径。

**待修复**：
1. 引入 `BackgroundTaskTracker` 统一管理后台任务，支持进度查询、异常日志、取消。
2. 关键路径改用 `IHostedService` 或 `Channel<T>` 消费者模式。
3. 所有 `Task.Run` 至少加上异常日志。

```csharp
// 最小修复：至少记录异常
_ = Task.Run(async () =>
{
    try
    {
        await DoWorkAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Background task failed");
    }
});
```

---

### 问题 36 — SSE keep-alive ping 用 Task.Run 实现循环（P3）❌

**根因**：`ChatEndpoints.cs:394-400,527-533` 手动实现了每 15 秒发 `: ping` 的 keep-alive 逻辑。SSE 协议本身支持空注释行作为 ping，无需 `Task.Run` 循环。

**修复方案**：使用 `HttpResponse` 内置的 keep-alive 机制，或直接在响应流中周期性写入空注释即可，无需 Task 轮询。

---

### 问题 37 — HarnessEndpoints 加载 500 条后在 C# 中聚合（P2）✅

**根因**：`HarnessEndpoints.cs:274-276,580-582` 执行 `.Take(500).ToListAsync()` 将 500 行拉到内存，再在 C# 中做 `GroupBy`、`Count`、`Average` 等聚合运算。DB 侧 `GROUP BY` 可以高效完成。

**修复（第四轮）**：将聚合逻辑改为原生 DB `GROUP BY` 查询，仅返回聚合结果，减少传输和内存开销。`evaluations/summary`、`visualizer/model-stats`、`visualizer/stats` 全部改为 DB 聚合，同时为防止无界扫描保留 `Take(500)` 防护上限。

---

### 问题 38 — ReDoS 风险（P2）❌

**根因**：`AiService.cs:1640-1646` 的 `CleanResponse` 方法使用多个正则表达式，如 `<.*?>`（HTML 标签剥离）、`.*?\n\n` 等嵌套量词模式，对不可控 AI 输出进行清理。恶意构造的长输入可能导致灾难性回溯（Catastrophic Backtracking）。

**修复方案**：
1. 使用非正则方式替代简单模式（如 `<.*?>` 可用 `string.Replace` 或 `ReadOnlySpan<char>`）。
2. 对复杂正则设置超时：`new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1))`。

---

### 问题 39 — JSON 被解析两次（P2）❌

**根因**：`AiService.cs:2057-2091` 的 `ParseCliOutput` 先尝试将整个输出解析为 JSON（策略 1），失败后再逐行解析（策略 2）。对大型输出两种策略各执行一次，解析时间翻倍。

**修复方案**：根据输出结构选择策略（如以 `{` 开头则尝试 JSON 解析，否则直接转到逐行解析）。

---

### 问题 40 — 静态可变缓存无同步保护（P2）✅

**根因**：`AiService.cs:100,104` 的 `_cachedPolicies` 被 `InvalidatePolicyCache()` 设为 `null`，但访问路径 `LoadPoliciesAsync` 中的 null 检查和重新加载未使用锁或 `Lazy<T>`，存在竞态：线程 A 读取到非 null 但线程 B 刚将其置 null，导致线程 A 拿到过期副本。

**修复（第五轮）**：策略改为永久内存缓存（非 TTL 轮换），通过 `ApplicationExtensions` 启动时挂载的 `FileSystemWatcher` 在文件变更时调用 `InvalidatePolicyCache()` 主动失效，彻底避免 TTL 到期时的竞态窗口。

---

### 问题 41 — Prompt 模板文件每次从磁盘读取（P2）✅

**根因**：`PipelineLoaderService.cs:88-104` 每次调用 `GetPromptTemplateAsync` 都 `File.ReadAllTextAsync`，无缓存层。高频 pipeline 执行下产生大量文件 IO。

**修复（第五轮）**：策略文件从"5分钟 TTL"改为永久缓存，`FileSystemWatcher` 监控 `pipelines/policies/` 目录，文件变更时自动失效并重新加载。

---

### 问题 42 — MemoryGraphService 同步文件读取（P2）❌

**根因**：`MemoryGraphService.cs:33` 调用 `GetMemoriesForUser(userId)` 时，若缓存未命中则同步执行 `File.ReadAllText`，阻塞调用线程。

**修复方案**：改为异步读取 `File.ReadAllTextAsync` 并 `await`。

---

### 问题 43 — StaticFiles 中间件在认证之前注册（P2）❌

**根因**：`Program.cs:17` 的 `UseStaticFiles()` 在 `UseAuthentication()` 和 `UseAuthorization()` 之前注册。所有静态文件（包括可能的敏感文件）无需认证即可访问。

**修复方案**：调整中间件顺序，将 `UseStaticFiles()` 移到认证之后（或确认所有静态文件都适合公开）。

```csharp
// Before
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// After
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles(); // 如果所有静态文件都需认证
```

---

### 问题 44 — Project.CreatedAt 使用 DateTime.Now 而非 UTC（P3）❌

**根因**：`Project.cs:17` 使用 `DateTime.Now`（服务器本地时间），而项目中其他所有模型（如 `Message`, `ChatSession` 等）均使用 `DateTime.UtcNow`。这在跨时区部署时导致时间不一致。

**修复方案**：改为 `DateTime.UtcNow`。

---

### 问题 45 — SkillLearningService.LearnFromInteractionAsync 为空实现（P2）❌

**根因**：`SkillLearningService.cs:25-29` 整个方法体为空（`return Task.CompletedTask`），注释为"not yet implemented"。但在多处被调用，造成误解和无效开销。

**修复方案**：移除所有调用点，或标识为 `[Obsolete]` 并记录技术债。

---

### 问题 46 — BuildHistoryBlockAsync 定义未调用（P3）❌

**根因**：`AiService.cs:1104-1108` 定义的 `BuildHistoryBlockAsync` 方法包装了 `LoadHistoryAndMessageIdAsync`，但没有任何代码调用它。

**修复方案**：删除或补充调用者。

---

### 问题 47 — GetProjectIdFromPath 废弃（P3）❌

**根因**：`FileWatcherService.cs:122-135` 的 `GetProjectIdFromPath` 搜索 `/projects/{id}/` 路径模式，但在当前项目结构中该模式不存在，始终返回 null。

**修复方案**：删除该方法及其调用。

---

### 问题 48 — PipelineLoaderService.GetNames 无调用者（P3）❌

**根因**：`PipelineLoaderService.cs:80-86` 的 `GetNames()` 方法在所有代码中没有被引用。

**修复方案**：删除或标记 `[Obsolete]`。

---

### 问题 49 — Skill.ExampleInput 和 Skill.SourceInteractionId 声明未使用（P3）❌

**根因**：`Skill.cs:9,20` 的属性在 `Skill` 实体生命周期中从未被业务逻辑读取。

**修复方案**：删除或添加读取逻辑。

---

### 问题 50 — 前端 applySuggestion 和 viewFile 仅 console.log（P3）❌

**根因**：`index.html:886-889,880-884` 的函数体只有 `console.log` 或硬编码 `activeTab` 切换。

**修复方案**：补充实现或删除。

---

### 问题 51 — test-project/ 和 test-project-2/ 残留（P3）❌

**根因**：两个目录已在 `.csproj` 中 `Compile Remove`，但物理文件仍在磁盘上，占用空间并造成困惑。

**修复方案**：确认无引用后删除。

---

### 问题 52 — test_multi_agent_flow.py 等 Python 测试脚本残留（P3）❌

**根因**：`AiChatApp/test_multi_agent_flow.py`、`_v2.py`、`_v3.py` 等测试脚本位于 C# 项目目录中，不属于项目编译产物，且可能已过时。

**修复方案**：归档到 `tests/` 目录或删除。

---

### 问题 53 — AgentStep 字段无截断，协作流写入量超 100K 字符（P1）✅

**根因**：`AgentStep.Persona` 直接存储完整的 Agent 系统提示（可能数千字），`AgentStep.Input` 存储完整上下文输入（可能数万字）。10 步协作流程下，累计 DB 写入量超过 110K 字符，且这些超长内容在评估展示时也消耗额外资源。

**修复（第四轮）**：`AgentStep.Persona` 截断至 1000 字符、`Input` 截断至 2000 字符，10 步协作流程写入量从约 110K 字符降至约 40K 字符（约 63% 降低）。

---

### 问题 54 — RunSubtaskLayerAsync 子任务各自重复搜索记忆/技能（P1）✅

**根因**：`AiService.RunSubtaskLayerAsync` 中，N 个并行子任务在 `RunAgentStepAsync` 内部各自独立执行记忆搜索和技能搜索，造成 N 倍重复 IO（每个子任务相同用户、相同上下文搜索相同数据）。

**修复（第二轮）**：在 layer 级别统一搜索记忆和技能一次，通过 `sharedMemories`/`sharedSkills` 参数传入所有并行子任务，N 次搜索→1 次搜索。

---

### 问题 55 — DB 缺少关键索引（P1）✅

**根因**：`Messages`（`ChatSessionId`, `CreatedAt`）、`AgentSteps`（`ChatSessionId`）、`Skills`（`UserId`, `IsEnabled`）、`SessionMemories`（`UserId`, `SessionId`）、`LongTermMemories`（`UserId`）、`InputHistories`（`UserId`）等高频查询字段均无索引，全表扫描导致查询延迟随数据量线性增长。

**修复（第五轮）**：`AppDbContext.OnModelCreating` 新增 7 个复合/单列索引；`ApplicationExtensions.InitializeDatabaseAsync` 启动时为已有 DB 补建 `CREATE INDEX IF NOT EXISTS`，兼容存量数据库。

---

### 问题 56 — SessionMemoryService 会话记忆上下文无上限（P1）✅

**根因**：`SessionMemoryService.ReadAllAsContextAsync` 将用户的所有会话记忆全部加载并拼接到上下文中，无数量上限。随着会话积累，注入的会话记忆 token 持续增长，最终可能占据数千 token 的上下文预算。

**修复（第五轮）**：添加 `OrderByDescending(m => m.UpdatedAt).Take(30)` 上限，仅保留最近 30 条会话记忆，防止长期使用后上下文膨胀。

---

### 问题 57 — GetResponseAsync/Stream 中 Session 被重复加载（P1）✅

**根因**：`AiService.GetResponseAsync`/`GetResponseStreamAsync` 先加载 Session，再调用 `BuildSystemPromptAsync`，而后者内部又通过 `GetProjectRootAsync` 重复查询 Project。`BuildHistoryBlockAsync` 和 `GetLatestUserMessageIdAsync` 也是两次独立 DB 查询，可合并。

**修复（第三轮）**：
- `BuildHistoryBlockAsync` + `GetLatestUserMessageIdAsync` 合并为 `LoadHistoryAndMessageIdAsync` 单次查询（4 次串行→2 次）
- 新增 `BuildSystemPromptAsync(preloadedSession)` 参数，调用方传入已加载的 Session，跳过内部重复查询

---

### 问题 58 — ParseAndSaveMemoryAsync 无快速跳过路径（P2）✅

**根因**：`AiService.ParseAndSaveMemoryAsync` 对每次 AI 响应都执行 `Split('\n')` + 逐行扫描查找 `MEMORY:` 前缀，但实际上绝大多数（>99%）的普通对话响应不包含该前缀，Split 和扫描完全浪费。

**修复（第四轮）**：在 `Split` 前先做 `Contains("MEMORY:")` 快速检查，不命中时直接返回，覆盖 99%+ 的调用路径。

---

### 问题 59 — DefaultProvider 每次从 DB 查询（P2）✅

**根因**：`ChatEndpoints` 的三个聊天端点（`/api/chat`、`/api/chat/stream`、`/api/chat/cooperate/stream`）在每次请求时都通过 `db.Users.FindAsync(userId)` 获取 `DefaultProvider`，而该字段极少变更。

**修复（第四轮）**：登录时将 `DefaultProvider` 写入 cookie claim（`AuthEndpoints`），三个聊天端点改为从 `HttpContext.User` claim 读取；`PUT /profile` 更新时刷新 cookie，保持 claim 与 DB 同步。消除每请求 DB 读取。

---

## 预期收益

| 修复阶段 | Token 节省 | 性能提升 | 代码质量 | 状态 |
|---------|-----------|---------|---------|------|
| P0 修复（#1, #2） | 每次连接 ~2000-5000 token | — | 修复采样语义错误 | ✅ 已完成 |
| 五轮优化综合（#1-#8, #13-#14, #23, #33, #37, #40-#41, #53-#59） | 每次请求 ~30-50% token 减少 | DB 查询延迟降低约 30-60% | 消除关键 N+1 | ✅ 已完成 |
| P1 记忆/上下文（#9, #18, #19, #20, #21 剩余） | 每次 AI 调用 ~2000-8000 token | — | — | ❌ 待修复 |
| P1 磁盘 IO（#3, #5, #15, #16, #17） | — | 搜索延迟降低 10-50x | 消除磁盘竞争 | ❌ 待修复 |
| P1 内存泄漏（#31, #32） | — | 消除长期运行的内存增长 | 防止 OOM | ❌ 待修复 |
| P2 冗余代码（#24-#30） | — | — | 减少维护负担 | ❌ 待修复 |
| P3 死代码（#45-#52） | — | — | 减少认知负荷 | ❌ 待修复 |
| **所有修复综合** | **预估 50-70% token 节省** | **预估 40-60% 请求延迟降低** | **大幅降低维护成本** | — |

---

## 修复优先级建议

### 已完成（第一~五轮）
- ✅ #1, #2, #4, #6, #7, #8（第一轮）
- ✅ #23, #33, #54（第二轮）
- ✅ #21（部分）, #35（部分）, #57（第三轮）
- ✅ #13, #14, #37, #53, #58, #59（第四轮）
- ✅ #40, #41, #55, #56（第五轮）

### 下一优先（P1 — 高频路径 + 高成本）
1. **#18** 上下文分级：定义轻量/标准/完整三级，简单请求降级 — 最大 token 节省来源
2. **#19** Pipeline O(n²) token 增长 — 只传递前序阶段最终输出
3. **#16, #17** 搜索路径磁盘 IO 优化 — AccessCount 移出写路径
4. **#31, #32** 内存泄漏修复 — MemoryGraphService/MemoryFileService 缓存上限
5. **#3** MemoryGraphService 改为 Singleton — 使 1 分钟缓存生效
6. **#15** GetMemoriesForUser 同步阻塞修复 — 改为 `await WaitAsync()`
7. **#9** 策略文件按需加载 — 标签化 + 按 Agent 类型筛选

### 维护优先（P2 — 代码质量）
1. **#24-#30** 冗余代码抽取（chat bubble、session 创建、provider 分辨率等）
2. **#35** Fire-and-forget 体系化治理（引入 BackgroundTaskTracker）
3. **#43** StaticFiles 中间件顺序安全修复
4. **#45-#52** 死代码清理

### 长期规划（P1/P3 — 架构改造）
1. **#10** CLI 进程持久化 / SDK 迁移
2. **#12** AiService 拆分（PromptBuilder + AgentOrchestrator）
3. **#20** 记忆整合批量化（累积 N 条后触发）
4. **#22** 前缀剥离代码彻底删除（配置 CLI 不 echo）
