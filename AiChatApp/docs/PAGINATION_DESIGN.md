# 分页加载详细设计文档

## 概述

AiChatApp 所有列表页面均实现了基于 HTMX 的**无限滚动（Infinite Scroll）分页**，无需用户手动点击"下一页"。当用户滚动到列表底部时，自动加载下一批数据并追加到 DOM。

---

## 统一分页模式

### 后端模式（ASP.NET Core Minimal API）

所有分页端点遵循同一模式：

```csharp
group.MapGet("/", async (... [FromQuery] int? page, [FromQuery] int? pageSize) => {
    var p = page ?? 1;
    var ps = pageSize ?? 20;

    var items = await db.Items
        .Where(...)
        .OrderByDescending(i => i.CreatedAt)
        .Skip((p - 1) * ps)
        .Take(ps + 1)           // 多取1条，用于检测是否有更多
        .ToListAsync();

    var hasMore = items.Count > ps;
    var itemsToReturn = items.Take(ps).ToList();

    return Results.Content(BuildListHtml(itemsToReturn, p, ps, hasMore), "text/html");
});
```

**关键点**：
- `Take(ps + 1)` 而非 `Take(ps)`：多取1条数据，若返回数量 > pageSize，说明还有更多
- 实际只渲染 `Take(ps)` 条记录
- `hasMore` 标志用于决定是否在最后一条记录上附加无限滚动属性

### 前端模式（HTMX Infinite Scroll）

在最后一条记录（`isLast == true`）的 HTML 元素上附加：

```html
hx-get='/api/endpoint?page=2&pageSize=20'
hx-trigger='revealed'
hx-swap='afterend'
```

- `hx-trigger='revealed'`：元素进入视口时触发
- `hx-swap='afterend'`：新内容追加到当前元素之后（不替换）
- 下一页同样会在其最后一条记录上附加 `page=3` 的触发属性，形成链式无限滚动

---

## 各页面实现细节

### 1. 聊天会话列表 `/api/chat/list`

**文件**: `Endpoints/ChatEndpoints.cs:37`

```
GET /api/chat/list?projectId=1&page=1&pageSize=20
```

- 按会话最新消息时间排序（`LastActivity` 降序）
- 仅显示有消息记录的会话
- 支持按 `projectId` 过滤

### 2. 聊天消息历史 `/api/chat/session/{id}`

**文件**: `Endpoints/ChatEndpoints.cs:96`

与其他页面不同，消息历史使用**"Load More"按钮**而非无限滚动：

```
GET /api/chat/session/{id}            # 最新20条消息
GET /api/chat/messages/{id}/before/{beforeId}  # 加载更旧的消息
```

- 消息按时间降序取最新20条，显示时反转为升序
- 点击"Load More"按钮加载 `beforeId` 之前的消息
- 原因：聊天界面滚动方向与列表相反（底部为最新），需要特殊处理

### 3. 记忆列表 `/api/memories`

**文件**: `Endpoints/MemoryEndpoints.cs:14`

```
GET /api/memories?page=1&pageSize=20
```

- 数据来源：`MemoryFileService`（文件系统，非数据库）
- 按文件名排序
- 记忆卡片展示标签、相关度分数、访问次数

### 4. 笔记列表 `/api/notes`

**文件**: `Endpoints/NotesEndpoints.cs:18`

```
GET /api/notes?page=1&pageSize=20
```

- 按 `UpdatedAt` 降序排序（最近修改的在前）
- 列表显示标题和时间
- 点击笔记加载详情到右侧面板（`#note-content`）

### 5. 技能列表 `/api/skills`

**文件**: `Endpoints/SkillEndpoints.cs:12`

```
GET /api/skills?page=1&pageSize=20
```

- 数据来源：`SkillManagerService`（合并 DB + 文件系统技能）
- 展示系统技能（只读）和用户技能（可编辑/删除）

### 6. 待办事项列表 `/api/todos`

**文件**: `Endpoints/TodoEndpoints.cs:25`

```
GET /api/todos?page=1&pageSize=20
```

- 按 `CreatedAt` 降序排序
- 新建 Todo 时直接在列表顶部插入（`hx-swap='afterbegin'`），不重新加载整页

### 7. 输入历史 `/api/input-history`

**文件**: `Endpoints/InputHistoryEndpoints.cs:16`

```
GET /api/input-history?page=1&pageSize=20
```

- 按 `UsedAt` 降序排序（最近使用的在前）
- 显示于聊天输入历史弹窗
- 点击历史记录自动填充聊天输入框

### 8. 文件管理器 `/api/files`

**文件**: `Endpoints/FileManagerEndpoints.cs:16`

```
GET /api/files?path=/some/path&root=/base&page=1&pageSize=20
```

- 先显示目录，再显示文件（`OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name)`）
- 仅首页（`page=1`）渲染面包屑导航和头部区域
- 后续分页仅追加文件条目，避免重复渲染导航

---

## 数据流图

```
用户滚动到列表底部
        │
        ▼
最后一条元素进入视口（HTMX detected 'revealed'）
        │
        ▼
HTMX 发送 GET /api/endpoint?page=2&pageSize=20
        │
        ▼
后端执行 Skip(20).Take(21) 查询
        │
        ├─── hasMore=true → 最后一条附加 page=3 的 revealed 触发器
        │
        └─── hasMore=false → 最后一条不附加触发器（滚动加载结束）
        │
        ▼
返回 HTML 片段，HTMX 用 afterend 追加到 DOM
```

---

## 性能考虑

- **默认 pageSize=20**：适合大多数屏幕高度，单次网络请求返回数据量适中
- **数据库索引**：排序字段（`CreatedAt`, `UpdatedAt`, `UsedAt`）应建立索引以避免全表扫描
- **`Take(ps+1)` 技巧**：避免额外的 `COUNT` 查询，用1次查询同时获取数据和 hasMore 信息
- **文件管理器特殊处理**：头部/面包屑仅在 `page==1` 时渲染，减少后续分页的响应体积

---

## 前端无限滚动触发机制

HTMX `revealed` 触发器利用 IntersectionObserver（由 HTMX 内部实现）检测元素是否进入视口：

```html
<!-- 最后一条记录示例 -->
<div
  hx-get='/api/todos?page=2&pageSize=20'
  hx-trigger='revealed'
  hx-swap='afterend'
  class="todo-item">
  <!-- 内容 -->
</div>
```

当该 `<div>` 进入视口，HTMX 自动发出请求，将下一页 HTML 插入其后。下一页的最后一条记录同样带有 `page=3` 的触发器，形成**链式无限加载**，直到 `hasMore=false` 时不再附加触发器，加载自动停止。

---

## 与传统分页的对比

| 维度 | 传统翻页 | 无限滚动（当前方案） |
|------|----------|----------------------|
| 用户操作 | 点击页码/下一页 | 自然滚动 |
| URL 状态 | 可书签/分享 | 无法直接定位特定页 |
| 数据库查询 | OFFSET（可能性能差） | OFFSET（相同，但分批） |
| 实现复杂度 | 低 | 中（需处理链式触发） |
| 适用场景 | 精确定位 | 浏览/探索类场景 |

当前应用以**浏览型交互**为主（记忆、历史、文件），无限滚动体验更流畅。
