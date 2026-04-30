using System.Net;
using System.Diagnostics;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class HarnessEndpoints
{
    public static void MapHarnessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/harness").RequireAuthorization();

        group.MapGet("/pipelines", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines");
            if (!Directory.Exists(path)) return Results.Ok(new List<string>());
            var files = Directory.GetFiles(path, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
            return Results.Ok(files);
        });

        group.MapGet("/pipelines/{name}", (string name) => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", $"{name}.json");
            if (!File.Exists(path)) return Results.NotFound();
            return Results.Text(File.ReadAllText(path), "application/json");
        });

        group.MapPost("/pipelines", async (HttpContext context) => {
            var form = await context.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var content = form["content"].ToString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(content)) return Results.BadRequest();
            
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", $"{name}.json");
            await File.WriteAllTextAsync(path, content);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapGet("/schemas", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "schemas");
            if (!Directory.Exists(path)) return Results.Ok(new List<string>());
            var files = Directory.GetFiles(path, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
            return Results.Ok(files);
        });

        group.MapGet("/schemas/{name}", (string name) => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "schemas", $"{name}.json");
            if (!File.Exists(path)) return Results.NotFound();
            return Results.Text(File.ReadAllText(path), "application/json");
        });

        group.MapPost("/schemas", async (HttpContext context) => {
            var form = await context.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var content = form["content"].ToString();
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "schemas", $"{name}.json");
            await File.WriteAllTextAsync(path, content);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapGet("/prompts", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "prompts");
            if (!Directory.Exists(path)) return Results.Ok(new List<string>());
            var files = Directory.GetFiles(path, "*.md")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
            return Results.Ok(files);
        });

        group.MapGet("/prompts/{name}", (string name) => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "prompts", $"{name}.md");
            if (!File.Exists(path)) return Results.NotFound();
            return Results.Text(File.ReadAllText(path), "text/markdown");
        });

        group.MapPost("/prompts", async (HttpContext context) => {
            var form = await context.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var content = form["content"].ToString();
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "prompts", $"{name}.md");
            await File.WriteAllTextAsync(path, content);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapGet("/policies", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies");
            if (!Directory.Exists(path)) return Results.Ok(new List<string>());
            var files = Directory.GetFiles(path, "*.md")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
            return Results.Ok(files);
        });

        group.MapGet("/policies/{name}", (string name) => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies", $"{name}.md");
            if (!File.Exists(path)) return Results.NotFound();
            return Results.Text(File.ReadAllText(path), "text/markdown");
        });

        group.MapPost("/policies", async (HttpContext context) => {
            var form = await context.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var content = form["content"].ToString();
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies", $"{name}.md");
            await File.WriteAllTextAsync(path, content);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapGet("/pipelines/html", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines");
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Select(Path.GetFileNameWithoutExtension).ToList() : new List<string?>();
            var html = $@"
                <div class='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4'>
                    {string.Concat(files.Select(f => $@"
                        <div class='card bg-base-100 shadow-sm border border-base-300'>
                            <div class='card-body p-4'>
                                <h3 class='card-title text-sm font-bold'>{f}</h3>
                                <div class='card-actions justify-end mt-2'>
                                    <button onclick=""editHarnessFile('pipelines', '{f}')"" class='btn btn-ghost btn-xs'>Edit JSON</button>
                                </div>
                            </div>
                        </div>"))}
                    <button onclick=""const n=prompt('Pipeline Name:'); if(n) editHarnessFile('pipelines', n)"" class='btn btn-dashed border-2 border-base-300 h-24 flex flex-col gap-2'>
                        <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='1.5' stroke='currentColor' class='w-6 h-6'><path stroke-linecap='round' stroke-linejoin='round' d='M12 4.5v15m7.5-7.5h-15' /></svg>
                        <span class='text-xs opacity-50 uppercase font-black'>New Pipeline</span>
                    </button>
                </div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/prompts/html", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "prompts");
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.md").Select(Path.GetFileNameWithoutExtension).ToList() : new List<string?>();
            var html = $@"
                <div class='grid grid-cols-1 md:grid-cols-2 gap-4'>
                    {string.Concat(files.Select(f => $@"
                        <div class='card bg-base-100 shadow-sm border border-base-300'>
                            <div class='card-body p-4'>
                                <h3 class='card-title text-sm font-bold'>{f}.md</h3>
                                <div class='card-actions justify-end mt-2'>
                                    <button onclick=""editHarnessFile('prompts', '{f}')"" class='btn btn-ghost btn-xs'>Edit Markdown</button>
                                </div>
                            </div>
                        </div>"))}
                </div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/schemas/html", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "schemas");
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Select(Path.GetFileNameWithoutExtension).ToList() : new List<string?>();
            var html = $@"
                <div class='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4'>
                    {string.Concat(files.Select(f => $@"
                        <div class='card bg-base-100 shadow-sm border border-base-300'>
                            <div class='card-body p-4'>
                                <h3 class='card-title text-sm font-bold'>{f}.json</h3>
                                <div class='card-actions justify-end mt-2'>
                                    <button onclick=""editHarnessFile('schemas', '{f}')"" class='btn btn-ghost btn-xs'>Edit Schema</button>
                                </div>
                            </div>
                        </div>"))}
                </div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/policies/html", () => {
            var path = Path.Combine(AppContext.BaseDirectory, "pipelines", "policies");
            var files = Directory.Exists(path) ? Directory.GetFiles(path, "*.md").Select(Path.GetFileNameWithoutExtension).ToList() : new List<string?>();
            var html = $@"
                <div class='grid grid-cols-1 md:grid-cols-2 gap-4'>
                    {string.Concat(files.Select(f => $@"
                        <div class='card bg-base-100 shadow-sm border border-base-300'>
                            <div class='card-body p-4'>
                                <h3 class='card-title text-sm font-bold'>{f}.md</h3>
                                <div class='card-actions justify-end mt-2'>
                                    <button onclick=""editHarnessFile('policies', '{f}')"" class='btn btn-ghost btn-xs'>Edit Policy</button>
                                </div>
                            </div>
                        </div>"))}
                    <button onclick=""const n=prompt('Policy Name:'); if(n) editHarnessFile('policies', n)"" class='btn btn-dashed border-2 border-base-300 h-24 flex flex-col gap-2'>
                        <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='1.5' stroke='currentColor' class='w-6 h-6'><path stroke-linecap='round' stroke-linejoin='round' d='M12 4.5v15m7.5-7.5h-15' /></svg>
                        <span class='text-xs opacity-50 uppercase font-black'>New Policy</span>
                    </button>
                </div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/evals/html", async (AppDbContext db) => {
            var all = await db.Evaluations.OrderByDescending(e => e.CreatedAt).Take(20).ToListAsync();
            var summary = all.Any() ? new { count = all.Count, avg = (double)all.Average(e => e.Score) } : new { count = 0, avg = 0.0 };
            
            var html = $@"
                <div class='space-y-6'>
                    <div class='stats shadow w-full bg-base-100 border border-base-300'>
                        <div class='stat'>
                            <div class='stat-title text-[10px] font-black uppercase opacity-40'>Total Evaluations</div>
                            <div class='stat-value text-primary'>{summary.count}</div>
                        </div>
                        <div class='stat'>
                            <div class='stat-title text-[10px] font-black uppercase opacity-40'>Avg Score</div>
                            <div class='stat-value text-secondary'>{summary.avg:F2}</div>
                        </div>
                    </div>
                    <div class='overflow-x-auto bg-base-100 rounded-2xl border border-base-300'>
                        <table class='table table-zebra table-sm'>
                            <thead>
                                <tr class='bg-base-200'>
                                    <th class='text-[10px] font-black uppercase opacity-40'>Criteria</th>
                                    <th class='text-[10px] font-black uppercase opacity-40'>Score</th>
                                    <th class='text-[10px] font-black uppercase opacity-40'>Reasoning</th>
                                    <th class='text-[10px] font-black uppercase opacity-40'>Date</th>
                                    <th class='text-[10px] font-black uppercase opacity-40'>Action</th>
                                </tr>
                            </thead>
                            <tbody>
                                {string.Concat(all.Select(e => $@"
                                    <tr>
                                        <td class='font-bold'>{e.Criteria}</td>
                                        <td>
                                            <div class='badge {(e.Score > 0.7 ? "badge-success" : e.Score > 0.4 ? "badge-warning" : "badge-error")} badge-outline font-black font-mono text-[10px]'>{e.Score:P0}</div>
                                        </td>
                                        <td class='text-xs italic opacity-70 max-w-xs truncate' title='{WebUtility.HtmlEncode(e.Reasoning)}'>{WebUtility.HtmlEncode(e.Reasoning)}</td>
                                        <td class='text-[10px] opacity-40'>{e.CreatedAt:MMM dd HH:mm}</td>
                                        <td>
                                            <button onclick='extractRule({e.Id})' class='btn btn-ghost btn-xs text-primary font-black uppercase text-[9px]'>Extract Rule</button>
                                        </td>
                                    </tr>"))}
                            </tbody>
                        </table>
                    </div>
                </div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/evaluations/recent", async (AppDbContext db) => {
            var evals = await db.Evaluations
                .Include(e => e.AgentStep)
                .OrderByDescending(e => e.CreatedAt)
                .Take(50)
                .ToListAsync();
            
            return Results.Ok(evals.Select(e => new {
                e.Id,
                e.Criteria,
                e.Score,
                e.Reasoning,
                e.CreatedAt,
                Role = e.AgentStep?.Role ?? "Unknown",
                StepId = e.AgentStepId
            }));
        });

        group.MapGet("/evaluations/summary", async (AppDbContext db) => {
            var all = await db.Evaluations.ToListAsync();
            if (!all.Any()) return Results.Ok(new { count = 0, avgScore = 0 });
            
            return Results.Ok(new {
                count = all.Count,
                avgScore = all.Average(e => e.Score),
                byCriteria = all.GroupBy(e => e.Criteria)
                    .Select(g => new { Criteria = g.Key, Avg = g.Average(e => e.Score) })
            });
        });

        // Add advanced visualizer and A/B test endpoints
        group.MapGet("/visualizer/model-stats", async (AppDbContext db) => {
            var steps = await db.AgentSteps.Include(s => s.Evaluations).ToListAsync();
            var stats = steps.GroupBy(s => new { s.Role, s.Model })
                .Select(g => new {
                    Role = g.Key.Role,
                    Model = g.Key.Model,
                    Count = g.Count(),
                    AvgScore = g.SelectMany(s => s.Evaluations).Any() ? g.SelectMany(s => s.Evaluations).Average(e => e.Score) : 0,
                    AvgDuration = g.Average(s => s.DurationMs),
                    SuccessRate = (double)g.Count(s => s.WasAccepted) / g.Count()
                }).OrderBy(s => s.Role).ThenBy(s => s.Model).ToList();
            
            return Results.Ok(stats);
        });

        group.MapGet("/visualizer/model-stats-html", async (AppDbContext db) => {
            // 获取所有步骤，包括 Provider 字段和 session 信息
            var stepsWithInfo = await db.AgentSteps
                .Include(s => s.Evaluations)
                .Join(db.Messages, s => s.MessageId, m => m.Id, (s, m) => new { Step = s, m.ChatSessionId })
                .Join(db.ChatSessions, x => x.ChatSessionId, cs => cs.Id, (x, cs) => new {
                    Step = x.Step,
                    SessionProvider = (cs.PreferredProvider ?? "gemini").ToLower()
                })
                .ToListAsync();

            // 辅助：标准化 provider 名称为显示名称
            string NormalizeProvider(string provider, string? model = null) {
                var p = provider.ToLower();
                var m = model?.ToLower() ?? "";
                
                // If the provider is explicitly copilot, return Copilot
                if (p.Contains("copilot")) return "Copilot";
                
                // If provider is gemini or model contains gemini
                if (p.Contains("gemini") || m.Contains("gemini")) return "Gemini";
                
                // If it's claude or model contains claude/anthropic/sonnet/haiku
                if (p.Contains("claude") || p.Contains("anthropic") || m.Contains("claude") || m.Contains("anthropic") || m.Contains("sonnet") || m.Contains("haiku") || m.Contains("opus")) return "Claude";
                
                // If it's codex
                if (p.Contains("codex") || m.Contains("codex")) return "Codex";
                
                // If it's opencode
                if (p.Contains("opencode") || p.Contains("open-code") || m.Contains("opencode") || m.Contains("open-code")) return "OpenCode";
                
                // If it's deepseek
                if (p.Contains("deepseek") || m.Contains("deepseek")) return "DeepSeek";

                // Special case for Copilot returning gpt-4 models but provider field being empty/incorrect
                if (p.Contains("gh-copilot") || m.Contains("gpt-4") && p == "copilot") return "Copilot";

                // If it's gpt/openai
                if (p.Contains("gpt") || p.Contains("openai") || m.Contains("gpt-") || m.Contains("o1-") || m.Contains("o3-")) return "OpenAI";
                
                return "Other";
            }

            // 确定每个 step 的显示名称
            string GetProviderDisplayName(AgentStep step, string sessionProvider) {
                // 优先使用新的 Provider 字段，并同时参考 Model
                return NormalizeProvider(
                    !string.IsNullOrEmpty(step.Provider) ? step.Provider : sessionProvider, 
                    step.Model
                );
            }

            // 2. 提供商配额设定 (软配额) - 为每个请求的工具设定独立配额
            var quotas = new Dictionary<string, long> {
                { "Gemini", 10000000 },
                { "Claude", 5000000 },
                { "Codex", 2000000 },
                { "Copilot", 2000000 },
                { "OpenCode", 5000000 },
                { "OpenAI", 2000000 },
                { "DeepSeek", 20000000 },
                { "Other", 1000000 }
            };

            // 3. UI 辅助逻辑
            string GetColor(string provider) {
                return provider switch {
                    "Gemini" => "text-blue-400",
                    "Claude" => "text-orange-400",
                    "Codex" => "text-emerald-400",
                    "Copilot" => "text-indigo-400",
                    "OpenCode" => "text-yellow-400",
                    "OpenAI" => "text-emerald-500",
                    "DeepSeek" => "text-cyan-400",
                    _ => "text-primary"
                };
            }

            // 4. 聚合数据 — 使用 GetProviderDisplayName 判断 provider
            var modelStats = stepsWithInfo
                .GroupBy(x => GetProviderDisplayName(x.Step, x.SessionProvider))
                .Select(g => {
                    var steps = g.Select(x => x.Step);
                    var promptT = steps.Sum(s => (long)s.PromptTokens);
                    var completionT = steps.Sum(s => (long)s.CompletionTokens);
                    var totalT = steps.Sum(s => (long)s.TotalTokens);
                    if (totalT == 0 && (promptT > 0 || completionT > 0)) totalT = promptT + completionT;
                    return new {
                        Model = g.Key,
                        Provider = g.Key,
                        Count = steps.Count(),
                        AvgScore = steps.SelectMany(s => s.Evaluations).Any() ? steps.SelectMany(s => s.Evaluations).Average(e => e.Score) : 0,
                        AvgDuration = steps.Any() ? steps.Average(s => s.DurationMs) : 0,
                        PromptTokens = promptT,
                        CompletionTokens = completionT,
                        TotalTokens = totalT,
                        SuccessRate = steps.Any() ? (double)steps.Count(s => s.WasAccepted) / steps.Count() : 0,
                        Roles = steps.GroupBy(s => s.Role).Select(rg => new { Role = rg.Key, Count = rg.Count() }).ToList()
                    };
                }).OrderByDescending(s => s.TotalTokens).ToList();

            // 5. 按提供商聚合 — 始终显示所有已知提供商（即使使用量为 0）
            var allProviders = new[] { "Gemini", "Claude", "Codex", "Copilot", "OpenCode" };
            var usageByProvider = modelStats.GroupBy(s => s.Provider).ToDictionary(g => g.Key, g => g);

            var providerStats = allProviders.Select(providerName => {
                var quota = quotas.ContainsKey(providerName) ? quotas[providerName] : 1000000;
                if (usageByProvider.TryGetValue(providerName, out var g)) {
                    var used = g.Sum(s => s.TotalTokens);
                    return new {
                        Provider = providerName,
                        Used = used,
                        Quota = quota,
                        Remaining = Math.Max(0, quota - used),
                        Percent = Math.Min(100, (double)used / quota * 100),
                        AvgSuccess = g.Average(s => s.SuccessRate),
                        ModelCount = g.Count(),
                        TotalCalls = g.Sum(s => s.Count),
                        Color = GetColor(providerName)
                    };
                }
                // Provider exists in config but no usage yet
                return new {
                    Provider = providerName,
                    Used = 0L,
                    Quota = quota,
                    Remaining = quota,
                    Percent = 0.0,
                    AvgSuccess = 1.0,
                    ModelCount = 0,
                    TotalCalls = 0,
                    Color = GetColor(providerName)
                };
            }).OrderByDescending(s => s.Used).ToList();

            var totalTokensAll = modelStats.Sum(s => s.TotalTokens);
            var totalCallsAll = modelStats.Sum(s => s.Count);

            var html = $@"
                <div class='space-y-8 animate-in fade-in slide-in-from-bottom-4 duration-500 pb-20'>
                    <!-- Header -->
                    <div class='flex flex-col md:flex-row md:items-center justify-between gap-4'>
                        <div>
                            <h2 class='text-2xl font-black uppercase tracking-tighter'>Global Provider Status</h2>
                            <p class='text-xs opacity-50 font-medium font-mono'>Last updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Real-time)</p>
                        </div>
                        <div class='flex items-center gap-3'>
                            <button onclick='htmx.trigger(""#agent-stats-container"", ""loadStats"")' class='btn btn-ghost btn-xs gap-2 opacity-50 hover:opacity-100 uppercase font-black'>
                                <svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' stroke-width='2.5' stroke='currentColor' class='w-3 h-3'><path stroke-linecap='round' stroke-linejoin='round' d='M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0 3.181 3.183a8.25 8.25 0 0 0 13.803-3.7M4.031 9.865a8.25 8.25 0 0 1 13.803-3.7l3.181 3.182m0-4.991v4.99' /></svg>
                                Refresh Data
                            </button>
                            <div class='bg-base-300/50 px-4 py-2 rounded-2xl border border-base-content/5 backdrop-blur-xl'>
                                <div class='text-[10px] font-black opacity-30 uppercase'>Global Consumption</div>
                                <div class='text-xl font-black font-mono text-primary'>{totalTokensAll:N0}</div>
                            </div>
                        </div>
                    </div>

                    <!-- Provider Overview Grid -->
                    <div class='grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4'>
                        {string.Concat(providerStats.Select(p => $@"
                            <div class='card bg-base-100 border border-base-content/10 shadow-xl overflow-hidden group hover:border-primary/30 transition-all'>
                                <div class='absolute top-0 right-0 p-3'>
                                    <div class='flex items-center gap-1'>
                                        <div class='w-2 h-2 rounded-full {(p.AvgSuccess > 0.9 ? "bg-success" : p.AvgSuccess > 0.7 ? "bg-warning" : "bg-error")} animate-pulse'></div>
                                        <span class='text-[8px] font-black uppercase opacity-40'>{(p.AvgSuccess > 0.9 ? "Healthy" : "Degraded")}</span>
                                    </div>
                                </div>
                                <div class='card-body p-5'>
                                    <div class='flex items-center gap-3 mb-4'>
                                        <div class='w-12 h-12 rounded-2xl bg-base-200 flex items-center justify-center {p.Color} font-black text-2xl shadow-inner border border-base-content/5'>
                                            {p.Provider[0]}
                                        </div>
                                        <div>
                                            <h3 class='font-black uppercase tracking-tight'>{p.Provider}</h3>
                                            <div class='text-[10px] opacity-40 font-bold'>{p.ModelCount} Active Models</div>
                                        </div>
                                    </div>

                                    <div class='space-y-3'>
                                        <div>
                                            <div class='flex justify-between text-[10px] font-black mb-1 opacity-60'>
                                                <span>TOKEN QUOTA</span>
                                                <span>{p.Percent:F1}%</span>
                                            </div>
                                            <progress class='progress {(p.Percent > 90 ? "progress-error" : p.Percent > 70 ? "progress-warning" : "progress-primary")} h-2 w-full' value='{p.Percent}' max='100'></progress>
                                        </div>
                                        <div class='grid grid-cols-2 gap-4 pt-2'>
                                            <div>
                                                <div class='text-[9px] font-black opacity-30 uppercase'>Used</div>
                                                <div class='text-sm font-black font-mono'>{p.Used:N0}</div>
                                            </div>
                                            <div>
                                                <div class='text-[9px] font-black opacity-30 uppercase'>Remaining</div>
                                                <div class='text-sm font-black font-mono text-success'>{p.Remaining:N0}</div>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                                <div class='bg-base-200/50 px-5 py-2 flex justify-between items-center border-t border-base-content/5'>
                                    <span class='text-[9px] font-black opacity-40 uppercase'>Success Rate</span>
                                    <span class='text-xs font-black font-mono'>{p.AvgSuccess:P0}</span>
                                </div>
                            </div>
                        "))}
                    </div>

                    <!-- Detailed Model Stats -->
                    <div class='mt-12 space-y-6'>
                        <h3 class='text-lg font-black uppercase tracking-widest opacity-50 pl-2 border-l-4 border-primary'>Detailed Model Analysis</h3>
                        <div class='grid grid-cols-1 gap-6'>
                            {string.Concat(modelStats.Select(s => $@"
                                <div class='settings-card group overflow-hidden border border-base-content/5'>
                                    <div class='flex flex-col lg:flex-row gap-6'>
                                        <!-- Left: Model Info & Success -->
                                        <div class='lg:w-1/3 space-y-4'>
                                            <div class='flex items-start justify-between'>
                                                <div>
                                                    <div class='badge badge-outline badge-xs font-black uppercase mb-1 opacity-50'>{s.Provider}</div>
                                                    <h3 class='text-xl font-black font-mono tracking-tighter text-primary group-hover:text-secondary transition-colors'>{s.Model}</h3>
                                                </div>
                                                <div class='radial-progress text-primary' style='--value:{s.SuccessRate * 100:F0}; --size:3.5rem; --thickness: 4px;'>
                                                    <span class='text-xs font-black'>{s.SuccessRate:P0}</span>
                                                </div>
                                            </div>
                                            
                                            <div class='grid grid-cols-2 gap-2'>
                                                <div class='bg-base-200/50 p-2 rounded-xl border border-base-content/5 text-center'>
                                                    <div class='text-[8px] font-black opacity-40 uppercase'>Avg Score</div>
                                                    <div class='text-lg font-black'>{(s.AvgScore > 0 ? s.AvgScore.ToString("P0") : "N/A")}</div>
                                                </div>
                                                <div class='bg-base-200/50 p-2 rounded-xl border border-base-content/5 text-center'>
                                                    <div class='text-[8px] font-black opacity-40 uppercase'>Avg Latency</div>
                                                    <div class='text-lg font-black font-mono text-xs'>{s.AvgDuration:F0}ms</div>
                                                </div>
                                            </div>

                                            <div class='flex flex-wrap gap-1'>
                                                {string.Concat(s.Roles.Select(r => $@"<span class='badge badge-ghost badge-xs opacity-50 font-bold'>{r.Role} ({r.Count})</span>"))}
                                            </div>
                                        </div>

                                        <!-- Right: Token Usage Breakdown -->
                                        <div class='flex-1 space-y-4 bg-base-200/30 p-4 rounded-2xl border border-base-content/5'>
                                            <div class='flex items-center justify-between'>
                                                <span class='text-[10px] font-black uppercase tracking-widest opacity-40'>Consumption Breakdown</span>
                                                <span class='text-xs font-mono font-bold text-primary'>{(s.TotalTokens > 0 ? s.TotalTokens.ToString("N0") + " Total" : "Tokens N/A")}</span>
                                            </div>

                                            <div class='space-y-3'>
                                                <div>
                                                    <div class='flex justify-between text-[10px] font-bold mb-1'>
                                                        <span>INPUT (PROMPT)</span>
                                                        <span class='opacity-50'>{(s.PromptTokens > 0 ? s.PromptTokens.ToString("N0") : "—")}</span>
                                                    </div>
                                                    <progress class='progress progress-primary h-2 w-full' value='{s.PromptTokens}' max='{Math.Max(1, s.TotalTokens)}'></progress>
                                                </div>
                                                <div>
                                                    <div class='flex justify-between text-[10px] font-bold mb-1'>
                                                        <span>OUTPUT (COMPLETION)</span>
                                                        <span class='opacity-50'>{(s.CompletionTokens > 0 ? s.CompletionTokens.ToString("N0") : "—")}</span>
                                                    </div>
                                                    <progress class='progress progress-secondary h-2 w-full' value='{s.CompletionTokens}' max='{Math.Max(1, s.TotalTokens)}'></progress>
                                                </div>
                                            </div>

                                            <div class='mt-6 pt-4 border-t border-base-content/5 grid grid-cols-3 gap-4'>
                                                <div class='text-center'>
                                                    <div class='text-xl font-black font-mono'>{s.Count}</div>
                                                    <div class='text-[8px] font-black opacity-30 uppercase'>Total Calls</div>
                                                </div>
                                                <div class='text-center'>
                                                    <div class='text-xl font-black font-mono'>{(s.Count > 0 ? (s.TotalTokens / (double)s.Count).ToString("N0") : "0")}</div>
                                                    <div class='text-[8px] font-black opacity-30 uppercase'>Tkn/Call</div>
                                                </div>
                                                <div class='text-center'>
                                                    <div class='text-xl font-black font-mono'>{((double)s.PromptTokens / Math.Max(1, s.TotalTokens)):P0}</div>
                                                    <div class='text-[8px] font-black opacity-30 uppercase'>Input Ratio</div>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            "))}
                        </div>
                    </div>
                </div>";
            return Results.Content(html, "text/html");
        });

        group.MapGet("/visualizer/stats", async (AppDbContext db) => {
            var steps = await db.AgentSteps.Include(s => s.Evaluations).ToListAsync();
            var stats = steps.GroupBy(s => s.Role)
                .Select(g => new {
                    Role = g.Key,
                    Count = g.Count(),
                    AvgScore = g.SelectMany(s => s.Evaluations).Any() ? g.SelectMany(s => s.Evaluations).Average(e => e.Score) : 0,
                    AvgDuration = 0 // Future: tracking duration
                }).ToList();
            
            return Results.Ok(stats);
        });

        group.MapGet("/compare", async (string promptA, string promptB, AppDbContext db) => {
            // Placeholder: In a real scenario, this would compare specific runs linked to different prompts
            var all = await db.Evaluations.OrderByDescending(e => e.CreatedAt).Take(20).ToListAsync();
            return Results.Ok(new {
                A = all.Where((e, i) => i % 2 == 0),
                B = all.Where((e, i) => i % 2 != 0)
            });
        });

        group.MapGet("/git/status", () => {
            try {
                var psi = new ProcessStartInfo("git", "status --porcelain pipelines/ memory/") {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppContext.BaseDirectory
                };
                using var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd();
                return Results.Ok(new { status = output });
            } catch (Exception ex) {
                return Results.Problem(ex.Message);
            }
        });

        group.MapPost("/git/commit", async (HttpContext context) => {
            var form = await context.Request.ReadFormAsync();
            var message = form["message"].ToString();
            if (string.IsNullOrEmpty(message)) return Results.BadRequest("Commit message required");

            try {
                var addPsi = new ProcessStartInfo("git", "add pipelines/ memory/") { WorkingDirectory = AppContext.BaseDirectory };
                Process.Start(addPsi)?.WaitForExit();

                var commitPsi = new ProcessStartInfo("git", $"commit -m \"{message.Replace("\"", "\\\"")}\"") { WorkingDirectory = AppContext.BaseDirectory };
                Process.Start(commitPsi)?.WaitForExit();

                return Results.Ok(new { message = "Committed successfully" });
            } catch (Exception ex) {
                return Results.Problem(ex.Message);
            }
        }).DisableAntiforgery();

        group.MapPost("/extract-rule", async (HttpContext context, AppDbContext db, AiService ai) => {
            var form = await context.Request.ReadFormAsync();
            if (!int.TryParse(form["evalId"], out var evalId)) return Results.BadRequest("Invalid evalId");

            var ev = await db.Evaluations.Include(e => e.AgentStep).FirstOrDefaultAsync(e => e.Id == evalId);
            if (ev == null) return Results.NotFound();

            string prompt = $@"
                Analyze the following AI evaluation failure and extract a specific, actionable 'Guardrail' rule in Markdown.
                This rule will be added to the 'Policies' folder to prevent this mistake in the future.
                
                FAILURE CONTEXT:
                Criteria: {ev.Criteria}
                Score: {ev.Score}
                Reasoning: {ev.Reasoning}
                
                AGENT STEP OUTPUT:
                {ev.AgentStep?.Output}
                
                OUTPUT FORMAT:
                Return ONLY the Markdown content for the rule. Do not include titles or explanations outside the markdown block.
            ";

            var rule = await ai.ExecuteCliDirectAsync(prompt, "claude");
            rule = System.Text.RegularExpressions.Regex.Replace(rule, @"^```markdown\n|```$", "").Trim();
            
            return Results.Ok(new { rule });
        }).DisableAntiforgery();
    }
}
