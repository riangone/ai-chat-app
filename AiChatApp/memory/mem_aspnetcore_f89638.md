---
name: aspnetcore,pathbase,caddy,pattern,deployment
description: ASP.NET Core 子路径部署模式：当 Caddy 等反向代理使用 `handle_path` 剥离路径前缀时，应用中的 `UsePathBase()` ...
type: user
userId: 1
tags: aspnetcore,pathbase,caddy,pattern,deployment
relevanceScore: 80
accessCount: 1
createdAt: 2026-04-30T10:57:06.4548148Z
lastAccessedAt: 2026-04-30T12:16:13.8163194Z
---

ASP.NET Core 子路径部署模式：当 Caddy 等反向代理使用 `handle_path` 剥离路径前缀时，应用中的 `UsePathBase()` 不会生效（因为请求到达时已无前缀）。

完整修复方案：
1. **推荐方案（Caddy 侧）**：将 `handle_path /nyf*` 改为 `handle /nyf*` + `reverse_proxy localhost:5001`，保留前缀转发给后端
2. **备选方案（应用侧）**：在 `Program.cs` 中添加兜底中间件，当 `PathBase` 为空时强制设置为 `/nyf`，同时在 Razor 视图中全面使用 `@Url.Content("~/...")` 而非硬编码路径
3. 为前端 JS 注入 `window.NetYamlForgeConfig.pathBase = "@Url.Content("~/").TrimEnd('/')"` 让客户端脚本也能正确构造 URL