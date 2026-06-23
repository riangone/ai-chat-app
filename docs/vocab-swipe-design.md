# 背单词 Swipe 功能 — 详细设计文档

## 1. 功能概述

类 Duolingo 的单词卡片学习页面，支持：
- 卡片翻转（正面显示单词，背面显示释义+例句）
- 左滑 = 不会（红色），右滑 = 会（绿色），点击 = 翻牌
- SRS 间隔复习算法（SM-2 简化版）
- 单词管理（增删改查）
- 学习统计（今日完成、连续天数、掌握率）

---

## 2. 数据模型

### VocabCard

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | int | 主键 |
| UserId | int | 所属用户 |
| Word | string | 正面：单词/词组 |
| Translation | string | 背面：释义 |
| Reading | string? | 注音（日语假名/拼音） |
| Example | string? | 例句 |
| ExampleTranslation | string? | 例句翻译 |
| Tags | string? | 逗号分隔标签 |
| Category | string? | 分类（JLPT N5, HSK1 等） |
| Level | int | SRS 等级 0–5 (0=新词, 5=已掌握) |
| TimesCorrect | int | 正确次数 |
| TimesWrong | int | 错误次数 |
| NextReviewAt | DateTime? | SRS 下次复习时间 |
| CreatedAt | DateTime | 创建时间 |
| UpdatedAt | DateTime | 更新时间 |

---

## 3. SRS 算法（SM-2 简化）

| Level | 下次复习间隔 |
|-------|------------|
| 0 | 立即（新词）|
| 1 | 1 天 |
| 2 | 3 天 |
| 3 | 7 天 |
| 4 | 14 天 |
| 5 | 30 天（已掌握）|

- 答对 → Level + 1（最高 5），更新 NextReviewAt
- 答错 → Level 重置为 0，NextReviewAt = 立即
- 今日待复习 = NextReviewAt <= Now || NextReviewAt == null

---

## 4. API 端点 (`VocabEndpoints.cs`)

```
GET    /api/vocab            → 返回所有单词列表 HTML 片段
GET    /api/vocab/review     → 返回今日待复习卡片 (JSON: [{id,word,translation,reading,example,level}])
POST   /api/vocab            → 新建单词卡片 (form: word, translation, reading?, example?, category?, tags?)
PUT    /api/vocab/{id}       → 更新卡片
DELETE /api/vocab/{id}       → 删除卡片
POST   /api/vocab/{id}/result → 提交复习结果 (form: result="correct"|"wrong")
GET    /api/vocab/stats      → 返回统计 HTML 片段
```

---

## 5. UI 设计

### 5.1 侧边栏入口
在 sidebar.html 的 `mindmap` 条目之后，添加：
```html
<div class="sidebar-item" onclick="openSlide('vocab')">
  📖 Vocab
</div>
```
sidebarIdx = 11（原 skills 及之后的 sidebarIdx 全部 +1）

### 5.2 SLIDE_DEFS 注册
```js
'vocab': { label: 'Vocab', sidebarIdx: 11 },
```
（mindmap 之后，skills 之前；skills 起 sidebarIdx += 1）

### 5.3 Swipe 卡片 UI 结构

```
[vocab slide]
  ├── Shell header: "📖 Vocab · N cards due"
  ├── Tab bar: [Study] [Cards] [Add]
  │
  ├── [Study Tab] #vocab-study-view
  │   ├── Progress bar (当前/总计)
  │   ├── Card container #vocab-card-container
  │   │   └── .vocab-card (单个卡片)
  │   │       ├── .card-inner (CSS 3D flip)
  │   │       │   ├── .card-front: 单词 + 分类标签
  │   │       │   └── .card-back: 释义 + 注音 + 例句
  │   │       └── 左滑/右滑 拖动动画
  │   ├── 手势提示: ← 不会 | 点击翻牌 | 会 →
  │   ├── 完成屏 #vocab-done-screen (全部复习完后)
  │   └── 底部按钮: [✗ 不会] [翻牌] [✓ 会]
  │
  ├── [Cards Tab] #vocab-cards-view
  │   ├── Search/Filter bar
  │   └── 卡片列表（level badge + word + translation + edit/delete）
  │
  └── [Add Tab] #vocab-add-view
      └── Form: word*, translation*, reading, example, category, tags
```

### 5.4 CSS 动画

```css
/* 3D 翻转 */
.vocab-card { perspective: 1000px; cursor: pointer; }
.card-inner { transform-style: preserve-3d; transition: transform 0.4s; }
.card-inner.flipped { transform: rotateY(180deg); }
.card-front, .card-back { backface-visibility: hidden; }
.card-back { transform: rotateY(180deg); }

/* 滑动动画 */
.vocab-card.swiping-right { transform: translateX(120%) rotate(15deg); transition: transform 0.3s; border-color: #22c55e; }
.vocab-card.swiping-left  { transform: translateX(-120%) rotate(-15deg); transition: transform 0.3s; border-color: #ef4444; }

/* 正确/错误反馈 */
.vocab-result-correct { background: rgba(34,197,94,0.1); }
.vocab-result-wrong   { background: rgba(239,68,68,0.1); }
```

### 5.5 JS 手势逻辑（Hammer.js）

```js
const hammer = new Hammer(cardEl);
hammer.get('pan').set({ direction: Hammer.DIRECTION_ALL });
hammer.on('pan', onPan);
hammer.on('panend', onPanEnd);
hammer.on('tap', flipCard);

function onPan(ev) {
  cardEl.style.transform = `translateX(${ev.deltaX}px) rotate(${ev.deltaX * 0.05}deg)`;
  // 颜色提示
}
function onPanEnd(ev) {
  if (ev.deltaX > 80) submitResult('correct');
  else if (ev.deltaX < -80) submitResult('wrong');
  else resetCard();
}
```

---

## 6. 文件变更清单

| 文件 | 操作 | 内容 |
|------|------|------|
| `Models/VocabCard.cs` | 新建 | VocabCard 数据模型 |
| `Data/AppDbContext.cs` | 修改 | 添加 `DbSet<VocabCard> VocabCards` |
| `Extensions/ApplicationExtensions.cs` | 修改 | 添加 VocabCards 表 CREATE TABLE + 索引 |
| `Endpoints/VocabEndpoints.cs` | 新建 | 全部 API 端点 |
| `Program.cs` | 修改 | `app.MapVocabEndpoints()` |
| `wwwroot/components/sidebar.html` | 修改 | 添加 Vocab 导航项 |
| `wwwroot/index.html` | 修改 | 添加 SLIDE_DEFS、vocab swiper slide HTML、JS 逻辑 |

---

## 7. 技术约束

- 不引入新的 npm 包；Hammer.js 已在 index.html 加载
- API 返回 HTML 片段（HTMX 风格），复习结果接口返回 JSON
- 认证：所有端点 `.RequireAuthorization()`，从 `ClaimsPrincipal` 取 UserId
- DB：通过 `ApplicationExtensions` 的 `CREATE TABLE IF NOT EXISTS` 创建，无 EF Migration
- 删除操作加 `hx-confirm`
