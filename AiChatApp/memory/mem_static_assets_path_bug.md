---
name: bug,frontend,static-assets,daisyui,css,js,path
description: NetYamlForge 项目中，前端静态资源（JS/CSS）路径缺少 `/nyf` 前缀，导致加载失败（如 `https://ai.0101.click/li...
type: user
userId: 0
tags: bug,frontend,static-assets,daisyui,css,js,path
relations: relevanceScore: 80
relevanceScore: 80
accessCount: 148
createdAt: 2026-04-30T11:04:48.0253430Z
lastAccessedAt: 2026-05-17T06:14:25.8091102Z
---

NetYamlForge 项目中，前端静态资源（JS/CSS）路径缺少 `/nyf` 前缀，导致加载失败（如 `https://ai.0101.click/lib/daisyui/daisyui.min.css` 应为 `https://ai.0101.click/nyf/lib/daisyui/daisyui.min.css`）。

**Why:** Caddy 使用 `handle_path /nyf*` 剥离了 `/nyf` 前缀后转发请求，ASP.NET Core 的 `PathBase` 为空，`~/` 占位符和 `@Url.Content("~/...")` 无法生成含 `/nyf` 的路径。

**How to apply:**
1. 在 `Program.cs` 中添加兜底中间件：若 `PathBase` 为空则强制设为 `/nyf`（解决 Caddy 剥离前缀的问题）
2. 在 Razor 视图（`_Layout.cshtml`、子项目布局等）中，将所有 `/lib/...` 硬编码路径改为 `@Url.Content("~/lib/...")`
3. 在 `window.NetYamlForgeConfig.pathBase` 中使用 `@Url.Content("~/").TrimEnd('/')` 动态传递路径前缀给前端 JS
4. 更新 `ProjectTemplateScaffolder.cs` 中的模板，确保新生成的子项目也具备同样的路径处理
5. 问题持续存在时，考虑在 Caddy 中改用 `handle`（保留前缀）而非 `handle_path`（剥离前缀）