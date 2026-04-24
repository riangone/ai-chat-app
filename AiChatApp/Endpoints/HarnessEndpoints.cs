using System.Net;
using AiChatApp.Data;
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
    }
}
