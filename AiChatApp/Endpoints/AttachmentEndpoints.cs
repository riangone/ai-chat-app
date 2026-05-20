using System.Security.Claims;
using AiChatApp.Services;

namespace AiChatApp.Endpoints;

public static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attachments").RequireAuthorization();

        group.MapPost("/upload", async (HttpContext context, AttachmentService svc, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var form = await context.Request.ReadFormAsync();
            var files = form.Files;

            if (files.Count == 0)
                return Results.BadRequest("No files provided.");
            if (files.Count > 5)
                return Results.BadRequest("Max 5 files per upload.");

            var results = new List<object>();
            var errors = new List<string>();

            foreach (var file in files)
            {
                var (att, err) = await svc.SaveAsync(file, userId);
                if (err != null)
                    errors.Add($"{file.FileName}: {err}");
                else if (att != null)
                    results.Add(new { id = att.Id, fileName = att.FileName, fileType = att.FileType, fileSize = att.FileSize, url = att.StoredPath });
            }

            if (results.Count == 0)
                return Results.BadRequest(string.Join("; ", errors));

            return Results.Ok(new { attachments = results, errors });
        }).DisableAntiforgery();

        group.MapDelete("/{id:int}", async (int id, AttachmentService svc, ClaimsPrincipal user) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var ok = await svc.DeleteAsync(id, userId);
            return ok ? Results.Ok() : Results.NotFound();
        });
    }
}
