using System.Security.Claims;
using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Endpoints;

public static class SnapshotEndpoints
{
    public static void MapSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").RequireAuthorization();

        group.MapGet("/{projectId}/snapshots", async (int projectId, ShadowGitService shadowGit, AppDbContext db, ClaimsPrincipal user) =>
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null) return Results.NotFound();
            var snapshots = await shadowGit.ListSnapshotsAsync(project.RootPath);
            return Results.Ok(snapshots);
        });

        group.MapGet("/{projectId}/snapshots/{hash}/diff", async (int projectId, string hash, ShadowGitService shadowGit, AppDbContext db) =>
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null) return Results.NotFound();
            var diff = await shadowGit.GetSnapshotDiffAsync(project.RootPath, hash);
            return Results.Ok(new { diff });
        });

        group.MapPost("/{projectId}/snapshots/{hash}/revert", async (int projectId, string hash, ShadowGitService shadowGit, AppDbContext db) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null) return Results.NotFound();
            var success = await shadowGit.RevertToSnapshotAsync(project.RootPath, hash);
            return success ? Results.Ok(new { message = "Reverted successfully." }) : Results.Problem("Revert failed.");
        });
    }
}
