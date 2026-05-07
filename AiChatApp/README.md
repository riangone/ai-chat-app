# AiChatApp — 多代理协作 AI 聊天平台

> 详细文档请见 [README_ZH.md](./README_ZH.md)

基于 ASP.NET Core 10 的 Web 聊天平台，统一封装 `gemini`、`claude`、`copilot`、`codex`、`opencode` 等 AI CLI，支持多代理 DAG 并行协作、Blackboard 共享工作空间、定向 Reviewer 返修和实时 SignalR 推送。

## 快速启动

```bash
# 构建
dotnet build AiChatApp

# 运行（http://localhost:5000）
dotnet run --project AiChatApp

# 重置数据库（schema 变更时）
rm AiChatApp/chat.db && dotnet run --project AiChatApp
```

默认管理员：`admin` / `admin123`（生产环境请设置 `ADMIN_INITIAL_PASSWORD` 环境变量）

## 环境要求

- .NET 10 SDK
- 至少一个 AI CLI 在 PATH：`gemini`（默认）、`claude`、`copilot`、`codex`、`opencode`
