# AiChatApp → 个人助理进化计划

**文档作者**: Hyperion (监督 AI)  
**创建日期**: 2026-05-19  
**目标**: 将系统从"被动聊天机器人"升级为"主动感知的个人助理"  
**执行分工**: Hyperion 负责规划与监督，opencode/gemini 负责代码实现

---

## 一、现状问题诊断（Bug + UX 缺陷）

### 🔴 P0 严重 Bug（必须先修）

#### BUG-001: SignalR 广播泄露所有用户数据
- **文件**: `Services/ProactiveBrainService.cs:38`
- **问题代码**: `await _hubContext.Clients.All.SendAsync("ProactiveMessage", suggestion);`
- **影响**: A 用户的 Todo 洞察、Git 分析结果广播给**所有在线用户**，严重隐私泄露
- **修复**: 将 `Clients.All` 替换为 `Clients.User(suggestion.UserId.ToString())`
- **前提条件**: `ProactiveSuggestion` 模型需增加 `UserId` 字段

#### BUG-002: ProjectPulseService 无用户上下文广播
- **文件**: `Services/ProjectPulseService.cs`
- **问题**: `AnalyzeProjectPulseAsync(diffSummary, userId: null)` 在 `userId=null` 时仍会广播
- **修复**: 无 userId 时跳过广播，或绑定到项目组播 `Clients.Group($"project-{projectId}")`

---

### 🟠 P1 严重用户体验问题

#### UX-001: 独立 Todo 页面与主界面 Slide 13 功能重叠
- **文件**: `wwwroot/todo/index.html`, `wwwroot/index.html` (Slide 13)
- **问题**: 两个 Todo 入口维护困难，用户不知该用哪个
- **方案**: 删除 `wwwroot/todo/index.html`（及其 `manifest.json`、`sw.js`），只保留主界面的 Slide 13

#### UX-002: 通知收件箱缺失
- **现状**: 主动洞察仅通过右下角弹窗呈现（`#proactive-suggestions`），消失后不可回看
- **影响**: 用户错过重要 AI 洞察，无法建立助理信任感
- **方案**: 新增"收件箱"侧边栏抽屉，统一管理所有洞察通知（见 Phase 2）

#### UX-003: 欢迎体验单薄
- **现状**: 登录后仅显示通用欢迎文字
- **影响**: 用户无法快速了解当前工作状态（今日待办、未读洞察数量）
- **方案**: 实现"晨间简报"欢迎卡片（见 Phase 2）

#### UX-004: 14 个 Swiper Slide 导航体验差
- **现状**: 功能通过 Swiper 轮播访问，深层功能（Harness 的 7 个子标签）难以发现
- **影响**: 功能发现率极低，移动端误触频繁
- **方案**: Phase 3 重构导航系统（短期不动）

#### UX-005: AI 只能"回答"不能"行动"
- **现状**: 用户说"帮我加个明天的提醒"→ AI 只文字回复，不实际创建 Todo
- **方案**: Phase 3 实现 Function Calling 工具调用（见详细规格）

---

## 二、改造计划

### Phase 1：修复安全 Bug + 清理重复页面（1-2 天）

#### Task 1.1: 修复 SignalR 广播安全漏洞

**目标文件**:
1. `Models/ProactiveSuggestion.cs` — 增加 `UserId` 字段
2. `Services/ProactiveBrainService.cs` — 所有 `SendSuggestionAsync` 调用处传入 userId，内部改为用户定向广播

**具体修改**:

```csharp
// Models/ProactiveSuggestion.cs — 增加字段
public int? UserId { get; set; }  // 新增

// Services/ProactiveBrainService.cs — 修改广播方法签名
public async Task SendSuggestionAsync(ProactiveSuggestion suggestion)
{
    if (suggestion.UserId.HasValue)
        await _hubContext.Clients.User(suggestion.UserId.Value.ToString())
                         .SendAsync("ProactiveMessage", suggestion);
    else
        // fallback: 不广播，记日志
        _logger.LogWarning("Proactive suggestion without userId skipped broadcast");
}

// 所有调用处补充 UserId：
// ProcessTodoChange: suggestion.UserId = item.UserId
// AnalyzeProjectPulseAsync: suggestion.UserId = userId
// ProcessNoteChange: suggestion.UserId = note.UserId (需 Note 模型有 UserId)
// ProcessWelcomeInsightAsync: suggestion.UserId = userId (参数已有)
```

#### Task 1.2: 删除独立 Todo 应用

**删除文件**:
- `wwwroot/todo/index.html`
- `wwwroot/todo/manifest.json`
- `wwwroot/todo/sw.js`

**注意**: 不需要修改任何后端代码，`/api/todos` API 保持不变，Slide 13 的前端逻辑保持不变。

---

### Phase 2：打造助理控制台（3-5 天）

#### Task 2.1: 持久化通知收件箱（后端）

**目的**: 主动洞察不再是"用完即弃"的 Toast，而是持久存储、可管理的收件箱消息。

**数据库变更**:
```csharp
// Models/ProactiveSuggestion.cs — 改造为可持久化实体
public class ProactiveSuggestion
{
    public int Id { get; set; }           // 主键（新增，原为 Guid string）
    public string Guid { get; set; } = Guid.NewGuid().ToString(); // 保留用于前端去重
    public int? UserId { get; set; }      // 关联用户（新增）
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "info";
    public bool IsRead { get; set; } = false;  // 新增
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Metadata { get; set; }
    public List<SuggestionAction> Actions { get; set; } = new();
}
```

**ApplicationExtensions.cs 补充 ALTER TABLE**:
```sql
ALTER TABLE ProactiveSuggestions ADD COLUMN UserId INTEGER;
ALTER TABLE ProactiveSuggestions ADD COLUMN IsRead INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ProactiveSuggestions ADD COLUMN Guid TEXT;
```

**新增 API 端点** (`Endpoints/InboxEndpoints.cs`):
```
GET  /api/inbox                  → 获取当前用户的收件箱（分页，未读优先）
POST /api/inbox/{id}/read        → 标记单条为已读
POST /api/inbox/read-all         → 全部标记已读
DELETE /api/inbox/{id}           → 删除单条
GET  /api/inbox/unread-count     → 返回未读数量（用于徽标）
```

**ProactiveBrainService 改造**:
- `SendSuggestionAsync` 改为：先将 suggestion 持久化到 DB，再通过 SignalR 推送实时通知
- SignalR 推送改为只推送"新消息ID + 摘要"，前端拉取完整内容

#### Task 2.2: 前端收件箱 UI

**修改文件**: `wwwroot/index.html`

**新增 UI 元素**:
1. **顶部导航栏** — 新增"铃铛"图标，显示未读数量红点
2. **收件箱抽屉** — 点击铃铛弹出 DaisyUI Drawer，列出所有洞察
3. **每条洞察卡片** — 显示类型图标、标题、摘要、时间戳、操作按钮
4. **实时更新** — SignalR 收到新消息时更新红点计数并在抽屉顶部插入新卡片

**收件箱卡片 HTML 模板**:
```html
<div class="card card-compact bg-base-100 shadow-sm border-l-4 border-primary" data-id="{id}">
  <div class="card-body">
    <div class="flex items-center gap-2">
      <span class="badge badge-{type}">{typeLabel}</span>
      <h3 class="font-semibold text-sm">{title}</h3>
      <span class="ml-auto text-xs opacity-50">{time}</span>
    </div>
    <p class="text-sm opacity-80">{content}</p>
    <div class="card-actions justify-end">
      {actionButtons}
    </div>
  </div>
</div>
```

#### Task 2.3: 晨间简报欢迎卡片

**触发时机**: 用户 SignalR 连接成功后（`OnConnectedAsync`）

**卡片内容**（从以下 API 聚合）:
1. 今日 Todo 数量及完成进度（`GET /api/todos` 筛选今日）
2. 未读洞察数量（`GET /api/inbox/unread-count`）
3. 最近一次 Git 提交摘要（从 ProjectPulseService 获取）

**后端接口** (`GET /api/assistant/briefing`):
```json
{
  "greeting": "早上好！今天是 2026-05-19，星期二。",
  "todayTodos": { "total": 5, "completed": 2 },
  "unreadInsights": 3,
  "lastCommit": "fix: 修复 SignalR 广播 Bug (2小时前)"
}
```

**前端展示**: 连接成功后在聊天面板顶部显示 1 张欢迎卡片（可一键关闭），不打断当前会话。

---

### Phase 3：自然语言驱动行动（Function Calling）（1-2 周）

#### Task 3.1: 定义内部工具集

**工具清单**（提供给 AI 作为可调用函数）:

| 工具名 | 触发描述 | 对应 API |
|--------|---------|---------|
| `create_todo` | "提醒我…"、"帮我加个任务…" | `POST /api/todos` |
| `complete_todo` | "完成了…"、"✓…" | `PUT /api/todos/{id}` |
| `save_note` | "记下这个"、"存为笔记" | `POST /api/notes` |
| `search_memory` | "我之前说过…"、"还记得…" | `GET /api/memories?q=` |
| `save_memory` | "记住这个"、"永远记得" | `POST /api/memories` |
| `list_todos` | "我今天有什么任务" | `GET /api/todos` |
| `open_session` | "打开…会话" | 前端路由 |

#### Task 3.2: 后端工具调用框架

**新增服务** `Services/AssistantToolService.cs`:
```csharp
public class AssistantToolService
{
    // 解析 AI 输出中的工具调用意图
    public Task<ToolCallResult?> ParseAndExecuteAsync(string aiResponse, int userId);
    
    // 内部工具实现
    private Task<string> CreateTodoAsync(string title, string? description, DateTime? dueDate, int userId);
    private Task<string> SaveNoteAsync(string title, string content, int userId);
    private Task<string> SearchMemoryAsync(string query, int userId);
}
```

**AiService 集成**:
- 在 `GetResponseAsync` / `GetResponseStreamAsync` 的后处理阶段调用 `AssistantToolService`
- 将工具定义注入系统提示（`BuildSystemPromptAsync` 追加工具描述）
- AI 通过约定格式输出工具调用（如 XML 标签或 JSON 块），后端解析执行

**系统提示工具描述模板** (追加到 `BuildSystemPromptAsync`):
```
## 可用工具
你可以通过以下格式在回复末尾触发系统操作：
<tool_call>{"name": "create_todo", "args": {"title": "...", "due_date": "2026-05-20"}}</tool_call>
执行后系统会自动确认操作结果，你无需另外说明。
```

#### Task 3.3: 前端工具调用反馈 UI

- AI 的工具调用成功后，在对话中显示确认气泡：`✓ 已创建任务「检查 API 性能」`
- 失败时显示错误气泡并提供手动链接

---

### Phase 4：导航系统现代化（可选，低优先级）

#### 背景
当前 14 个 Swiper Slide 设计不利于功能发现，特别是 Harness 面板嵌套 7 个子标签。

#### 方案：底部标签栏 + 侧边快速访问
- **保留 Swiper**（手势滑动）但增加**底部标签栏**（4-5 个主要区域图标）
- 主要区域：① 聊天 ② 工作区（Todo + Notes）③ 记忆 ④ 项目 ⑤ 设置
- 次要功能（Harness、Stats、CLI）收纳进"设置"或"更多"菜单
- 移动端 PWA 体验改善为"原生 App 感"

---

## 三、实现顺序与任务分配

```
Phase 1（本周）:
  [P0] Task 1.1 SignalR 安全修复          → opencode/gemini 实现
  [P0] Task 1.2 删除独立 Todo 页面         → opencode/gemini 实现

Phase 2（下周）:
  [P1] Task 2.1 持久化收件箱后端           → opencode/gemini 实现
  [P1] Task 2.2 收件箱前端 UI             → opencode/gemini 实现
  [P1] Task 2.3 晨间简报卡片              → opencode/gemini 实现

Phase 3（2-3 周后）:
  [P2] Task 3.1 工具定义                 → Hyperion 设计，opencode/gemini 实现
  [P2] Task 3.2 AssistantToolService     → opencode/gemini 实现
  [P2] Task 3.3 前端工具调用 UI           → opencode/gemini 实现

Phase 4（按需）:
  [P3] Task 4.1 导航系统重构              → 评估后决定
```

---

## 四、验收标准

### Phase 1 完成标准
- [ ] 用户 A 的 Todo 洞察**只发送给用户 A**（可通过两个浏览器窗口登录不同账号验证）
- [ ] `wwwroot/todo/` 目录已删除，访问 `/todo` 重定向到主界面
- [ ] `dotnet build` 无错误

### Phase 2 完成标准
- [ ] 主界面顶部有铃铛图标，显示未读洞察数量
- [ ] 点击铃铛打开收件箱抽屉，显示历史洞察列表
- [ ] 新建 Todo 触发的洞察出现在收件箱中，Toast 消失后仍可在收件箱回看
- [ ] 用户首次连接后在聊天区域看到晨间简报卡片
- [ ] DB 正确存储 ProactiveSuggestion 并关联 UserId

### Phase 3 完成标准
- [ ] 在聊天中输入"帮我创建一个任务：明天检查服务器性能"→ Todo 自动创建
- [ ] 聊天气泡中显示工具调用确认"✓ 已创建任务「明天检查服务器性能」"
- [ ] 工具调用不影响普通聊天的响应速度

---

## 五、注意事项与约束

1. **不要破坏现有功能**: 每个 Task 完成后运行 `dotnet build` 确认编译通过
2. **DB 变更使用 ALTER TABLE**: 遵循项目约定，在 `ApplicationExtensions.cs` 的 `InitializeDatabaseAsync` 中添加新列，不使用 EF Core 迁移
3. **新建 DB 时清理**: 如果 schema 变化太大，使用 `rm AiChatApp/chat.db && dotnet run` 重建
4. **前端保持 HTMX 风格**: 新增 UI 元素使用 HTMX + DaisyUI，不引入新的 JS 框架
5. **SignalR 用户标识**: `IHubContext.Clients.User(userId)` 依赖 `Context.UserIdentifier`，需确认认证配置正确映射用户 ID

---

*最后更新: 2026-05-19 by Hyperion*
