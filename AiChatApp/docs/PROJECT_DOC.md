# AiChatApp 项目文档

## 1. 项目概述

**AiChatApp** 是一个由 **Hyperion**（自主 AI 代理）驱动的现代化 Web 聊天应用，将多种专业 AI CLI 工具（gemini、copilot、claude、codex、opencode）封装为统一的 Web 界面，具有长期记忆、多智能体协作和高级会话管理功能。

### 技术栈
- **后端**: ASP.NET Core 10 Minimal API
- **数据库**: SQLite + Entity Framework Core
- **AI 集成**: 自定义 CLI 进程封装
- **前端**: HTMX + Tailwind CSS + DaisyUI
- **安全**: BCrypt 密码哈希 + Cookie 认证

## 2. 项目结构

```
AiChatApp/
├── Data/                      # EF Core DbContext
├── Models/                     # 数据模型
│   ├── Message.cs              # User, ChatSession, Message, AgentStep
│   ├── LongTermMemory.cs       # 长期记忆模型
│   ├── Skill.cs               # 技能模型
│   ├── AgentProfile.cs         # 代理配置
│   ├── Project.cs              # 项目模型
│   └── SessionMemory.cs        # 会话记忆
├── Services/                   # 核心服务
│   ├── AiService.cs            # AI 集成核心
│   ├── MemorySearchService.cs  # 记忆搜索
│   ├── MemoryConsolidationService.cs  # 自动记忆整合
│   ├── SkillManagerService.cs # 技能管理
│   └── SessionMemoryService.cs # 会话记忆
├── Endpoints/                  # API 端点
│   ├── ChatEndpoints.cs        # 聊天
│   ├── AuthEndpoints.cs        # 认证
│   ├── MemoryEndpoints.cs       # 记忆
│   └── SkillEndpoints.cs       # 技能
├── wwwroot/                    # 前端资源
│   ├── index.html              # 主界面
│   ├── login.html              # 登录
│   └── register.html            # 注册
├── pipelines/                  # 协作流水线配置
├── memory/                     # 记忆存储
└── Program.cs                  # 应用入口
```

## 3. 核心功能

### 3.1 多提供商支持
- `gemini`: Google Gemini CLI
- `copilot`: GitHub Copilot (gh extension)
- `claude`: Anthropic Claude CLI
- `codex`: OpenAI Codex CLI
- `opencode`: OpenCode CLI

支持自定义输出格式（如 `json`, `plain`, `stream-json`），通过 `--output-format` 参数传递给 CLI 工具。

### 3.2 协作模式 (Multi-Agent)
- **Orchestrator**: 规划任务
- **Executor**: 执行计划（支持最多2次重试）
- **Reviewer**: 审核结果

### 3.3 记忆系统
- **LongTermMemory**: 长期记忆，按标签检索
- **SessionMemory**: 会话级记忆
- **自动记忆整合**: AI 自动从对话中提取事实

### 3.4 技能系统
- 可开关的"插件"指令
- 支持触发关键词绑定
- 支持绑定到特定代理角色

## 4. API 端点

### 聊天
- `POST /api/chat` - 发送消息（非流式）
- `GET /api/chat/stream` - SSE 流式响应
- `GET /api/chat/cooperate/stream` - 协作模式 SSE

### 认证
- `POST /api/auth/register` - 用户注册
- `POST /api/auth/login` - 用户登录
- `POST /api/auth/logout` - 登出

### 记忆
- `GET /api/memory` - 获取记忆列表
- `POST /api/memory` - 保存记忆
- `DELETE /api/memory/{id}` - 删除记忆

### 技能
- `GET /api/skills` - 获取技能列表
- `POST /api/skills` - 创建技能
- `PUT /api/skills/{id}/toggle` - 开关技能

## 5. 构建与运行

```bash
# 构建
dotnet build AiChatApp

# 运行
dotnet run --project AiChatApp

# 重置数据库（如有架构变更）
rm AiChatApp/chat.db && dotnet run --project AiChatApp
```

访问 `http://localhost:5000`

## 6. 关键配置 (appsettings.json)

```json
{
  "AiSettings": {
    "DefaultProvider": "gemini",
    "FallbackProvider": "gemini",
    "TimeoutSeconds": 300,
    "SystemPrompts": {
      "default": "你是一个有帮助的AI助手..."
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=chat.db"
  }
}
```

## 7. 前端 UI

- **HTMX**: 无需重型 JS 框架的 SPA 体验
- **Tailwind CSS + DaisyUI**: 响应式设计，30+ 主题
- **Markdown 渲染**: 使用 marked.js
- **Drawer 布局**: 移动端友好

## 9. 最近改进 (Recent Improvements)

- **AI 动态简报**: 首页支持根据当前时间、待办进度和未读建议自动生成 AI 问候语。
- **简报缓存机制**: 实现 30 分钟缓存逻辑，减少 AI 调用频率，支持手动强制刷新。
- **CLI 输出格式支持**: `AiService` 支持 `--output-format` 参数（如 `plain`, `json`），提升解析效率。
- **修复待办跳转**: 修复了首页简报中“查看待办”按钮跳转到错误页面的 Bug。