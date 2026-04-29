# Hyperion 主动式增强方案 (v2.0) - 详细设计文档

## 1. 概述 (Overview)
为了提升 AI 代理的主动性并实现常驻后台运行，同时最大限度地优化 Token 消耗，本方案引入了 **“多级代理协同 (Multi-Agent Tiered Analysis)”** 架构。系统将不再盲目地对所有变动进行全量分析，而是通过低成本的“哨兵”逻辑触发高价值的“主脑”建议。

## 2. 核心架构：多代理分工 (Agent Specialization)

为了实现成本与能力的平衡，系统将主动任务拆分为以下三个角色：

| 代理角色 | 技术实现 | 职责 | 触发频率 | Token 策略 |
| :--- | :--- | :--- | :--- | :--- |
| **`Pulse-Trigger` (哨兵)** | C# 规则逻辑 (Regex/Stats) | 扫描 Todo 状态、编译错误日志、文件系统最后修改时间、Git 暂存区状态。 | 高 (每 5-10 分钟) | **零 Token**。纯本地逻辑，不调用 AI。 |
| **`Context-Aggregator` (记录员)** | 低参数量 AI 模型 (如 Gemini Flash) | 将哨兵收集的原始数据（Diff 片段、Todo 列表）清理并压缩为“上下文摘要”。 | 中 (仅当哨兵发现显著差异时) | **极低 Token**。使用简短的 Prompt，侧重于信息提取而非推理。 |
| **`Hyperion-Brain` (主脑)** | 高能力 AI 模型 (如 Claude 3.5 / Gemini Pro) | 基于压缩后的摘要进行架构层面的深度思考，生成具体的建议、风险预警或下一步任务规划。 | 低 (仅在需要推送高价值洞察时) | **按需调用**。确保每一条推送都具有高度的可执行性和准确性。 |

## 3. 实现组件 (Key Components)

### 3.1 定时脉搏服务 (`ProjectPulseService.cs`)
作为一个常驻的 `BackgroundService`，它是整个系统的驱动源：
- **监控周期**：配置化的 Interval（默认 15 分钟）。
- **状态快照**：维护一个简单的内存快照（Snapshot），用于对比两次扫描之间的增量变化。
- **触发逻辑**：只有当增量变化超过阈值（如：Todo 优先级变动、核心文件保存、编译失败）时，才激活后续代理。

### 3.2 启动洞察挂钩 (`WelcomeHook`)
集成在 `ProactiveAgentHub.cs` 的 `OnConnectedAsync` 中：
- 当用户打开 UI 连接到 SignalR 时，立即触发一次快速扫描。
- 返回一条“断点续传报告”，告知用户自上次会话以来的关键进展。

### 3.3 增强型 `AiService` (Profile 支持)
修改 `AiService` 以支持不同的代理配置文件：
- **Profile 定义**：包含 System Prompt、Model Name、Temperature 和 Max Tokens。
- **上下文注入**：自动关联 `SessionMemory` 和 `LongTermMemory`。

## 4. 逻辑工作流 (Workflow)

1. **Step 1: 本地扫描 (Sentinel)**  
   检查本地环境：`git status` + `TodoService.GetPendingTasks()` + `BuildLog.txt`。
2. **Step 2: 差异评估 (Evaluation)**  
   如果发现重要变动（例如：一个 Priority 为 High 的 Todo 被标记为完成），则进入 Step 3。
3. **Step 3: 上下文聚合 (Summarizer)**  
   调用记录员代理：“请总结过去 1 小时内 `AiService.cs` 的改动及其对现有任务的影响。”
4. **Step 4: 决策推送 (Brain)**  
   主脑判断是否需要打扰用户。如果判定为“重要且紧急”，则通过 SignalR 推送 `ProactiveMessage`。

## 5. Token 优化策略

- **增量分析**：仅发送变化的代码片段，而非整个文件。
- **短路逻辑**：如果记录员生成的摘要显示改动仅为格式调整，则立即终止流程，不调用主脑。
- **记忆剪裁**：在主动分析模式下，仅加载与当前变动文件相关的 `LongTermMemory`。

## 6. 待实施清单 (Implementation Roadmap)

1. [ ] **基础架构**：创建 `Models/AgentProfile.cs` 和 `Services/ProjectPulseService.cs`。
2. [ ] **逻辑升级**：在 `AiService.cs` 中实现多 Profile 切换逻辑。
3. [ ] **前端联动**：在 `index.html` 中优化建议面板，支持“思考中”状态的视觉反馈。
4. [ ] **哨兵开发**：实现针对 Todo 和文件系统的本地增量对比算法。

---
*文档版本：v2.0*  
*最后更新：2026-04-29*  
*由 Hyperion 自动生成*
