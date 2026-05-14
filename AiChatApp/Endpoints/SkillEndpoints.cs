using AiChatApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiChatApp.Endpoints;

public static class SkillEndpoints
{
    public static void MapSkillEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/skills").RequireAuthorization();

        group.MapGet("/", async (SkillManagerService skillManager, ClaimsPrincipal user, [FromQuery] int? page, [FromQuery] int? pageSize) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var p = page ?? 1;
            var ps = pageSize ?? 20;
            var allSkills = await skillManager.GetAllSkillsAsync(userId);
            var paged = allSkills.Skip((p - 1) * ps).Take(ps + 1).ToList();

            if (!allSkills.Any() && p == 1)
                return Results.Content("<div class='text-center py-10 opacity-40'>No skills defined yet.</div>", "text/html");

            var hasMore = paged.Count > ps;
            var skills = paged.Take(ps).ToList();

            return Results.Content(string.Concat(skills.Select((s, index) => {
                var systemBadge = s.IsSystem ? "<span class='badge badge-neutral badge-xs mb-1'>System</span>" : "<span class='badge badge-primary badge-xs mb-1'>User</span>";
                var isLast = index == skills.Count - 1 && hasMore;
                var scrollAttr = isLast ? $"hx-get='/api/skills?page={p + 1}&pageSize={ps}' hx-trigger='revealed' hx-swap='afterend'" : "";
                return $@"
                <div class='flex flex-col p-3 bg-base-200 rounded-lg group' {scrollAttr}>
                    <div class='items-start justify-between flex'>
                        <div class='flex-1'>
                            {systemBadge}
                            <div class='font-bold text-sm'>{s.DisplayName}</div>
                            <div class='text-[10px] opacity-60 mb-2'>{s.Description}</div>
                            <textarea class='textarea textarea-bordered textarea-xs w-full h-24 font-mono text-[10px] bg-base-300'
                                      id='prompt-{s.Name}' readonly>{s.Prompt}</textarea>
                        </div>
                        <div class='flex flex-col items-end gap-2 ml-2'>
                            <button onclick='editSkillPrompt(""{s.Name}"")' class='btn btn-ghost btn-xs text-primary' id='btn-edit-{s.Name}'>Edit</button>
                            <button onclick='saveSkillPrompt(""{s.Name}"", {s.IsSystem.ToString().ToLower()})' class='btn btn-primary btn-xs hidden' id='btn-save-{s.Name}'>Save</button>
                            {(!s.IsSystem ? $@"<button hx-delete='/api/skills/{s.Name}' hx-target='closest div' hx-swap='outerHTML'
                                    class='btn btn-ghost btn-xs text-error opacity-0 group-hover:opacity-100'>Delete</button>" : "")}
                        </div>
                    </div>
                </div>";
            })), "text/html");
        });

        group.MapPost("/save", async (HttpContext context, ClaimsPrincipal user, SkillManagerService skillManager) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var form = await context.Request.ReadFormAsync();
            var name = form["name"].ToString();
            var prompt = form["prompt"].ToString();
            var isSystem = form["isSystem"].ToString() == "true";
            await skillManager.SaveSkillAsync(name, prompt, userId, isSystem);
            return Results.Ok();
        }).DisableAntiforgery();

        group.MapDelete("/{name}", (string name, ClaimsPrincipal user, SkillManagerService skillManager) => {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            skillManager.DeleteSkill(name, userId, isSystem: false);
            return Results.Ok();
        });
    }
}
