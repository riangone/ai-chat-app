using AiChatApp.Data;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class PulseEndpoints
{
    public static void MapPulseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId}/pulse-ledger").RequireAuthorization();

        group.MapGet("/", async (int projectId, AppDbContext db, ClaimsPrincipal user, [Microsoft.AspNetCore.Mvc.FromQuery] int? page, [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
            if (project == null) return Results.NotFound();

            var pg = page ?? 1;
            var ps = pageSize ?? 20;

            var entries = await db.ProjectPulseLedgers
                .Where(l => l.ProjectId == projectId)
                .OrderByDescending(l => l.CreatedAt)
                .Skip((pg - 1) * ps)
                .Take(ps)
                .Select(l => new
                {
                    l.Id,
                    l.SourceType,
                    l.SourceKey,
                    l.Status,
                    l.BranchName,
                    l.ResultSummary,
                    l.CreatedAt,
                    l.CompletedAt
                })
                .ToListAsync();

            var total = await db.ProjectPulseLedgers.CountAsync(l => l.ProjectId == projectId);

            return Results.Ok(new { entries, total, page = pg, pageSize = ps });
        });

        // 手動トリガー: 10分周期の定期スキャンを待たずに、指定プロジェクトの pulse チェックを
        // その場で1回実行する。デバッグ/デモ用途。管理者のみ、かつ自分が所有するプロジェクトに限定。
        group.MapPost("/trigger", async (int projectId, AppDbContext db, ClaimsPrincipal user, ProjectPulseService pulseService, CancellationToken ct) =>
        {
            var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Results.Unauthorized();
            var userId = int.Parse(userIdStr);

            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);
            if (project == null) return Results.NotFound();

            var triggered = await pulseService.TriggerProjectPulseAsync(projectId, ct);
            if (!triggered) return Results.NotFound();

            return Results.Ok(new { message = "Pulse check triggered.", projectId });
        }).RequireAuthorization("AdminOnly");
    }
}
