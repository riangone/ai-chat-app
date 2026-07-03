using AiChatApp.Data;
using AiChatApp.Models;
using AiChatApp.Services.Infrastructure;
using ICliExecutorService = AiChatApp.Services.Infrastructure.ICliExecutor;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace AiChatApp.Endpoints;

public static class CourseEndpoints
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void MapCourseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/courses").RequireAuthorization();

        // GET /api/courses  — list courses with completion status
        group.MapGet("/", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var courses = await db.Courses
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Order)
                .Include(c => c.Lessons)
                .ToListAsync();

            var lessonIds = courses.SelectMany(c => c.Lessons.Select(l => l.Id)).ToList();
            var completed = await db.UserLessonProgresses
                .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId))
                .Select(p => p.LessonId)
                .Distinct()
                .ToListAsync();

            var completedSet = completed.ToHashSet();

            var result = courses.Select(c => new
            {
                c.Id,
                c.Title,
                c.Description,
                c.IconEmoji,
                c.Color,
                c.Order,
                TotalLessons = c.Lessons.Count,
                CompletedLessons = c.Lessons.Count(l => completedSet.Contains(l.Id)),
                Lessons = c.Lessons.OrderBy(l => l.Order).Select(l => new
                {
                    l.Id,
                    l.Title,
                    l.Order,
                    l.XpReward,
                    IsCompleted = completedSet.Contains(l.Id)
                }).ToList()
            });

            return Results.Json(result);
        });

        // GET /api/courses/xp  — user XP summary
        group.MapGet("/xp", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var totalXp = await db.UserLessonProgresses
                .Where(p => p.UserId == userId)
                .SumAsync(p => p.XpEarned);
            var streak = await GetStreakAsync(db, userId);
            return Results.Json(new { totalXp, streak, level = XpToLevel(totalXp) });
        });

        // GET /api/courses/lessons/{lessonId}/exercises
        group.MapGet("/lessons/{lessonId}/exercises", async (int lessonId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var lesson = await db.Lessons
                .Include(l => l.Course)
                .FirstOrDefaultAsync(l => l.Id == lessonId && l.Course!.UserId == userId);
            if (lesson == null) return Results.NotFound();

            var alreadyDone = await db.UserLessonProgresses
                .AnyAsync(p => p.UserId == userId && p.LessonId == lessonId);

            return Results.Json(new
            {
                lesson.Id,
                lesson.Title,
                lesson.XpReward,
                AlreadyCompleted = alreadyDone,
                Exercises = JsonSerializer.Deserialize<JsonElement>(lesson.ExercisesJson)
            });
        });

        // POST /api/courses/lessons/{lessonId}/complete
        group.MapPost("/lessons/{lessonId}/complete", async (int lessonId, ClaimsPrincipal user, AppDbContext db, int score) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var lesson = await db.Lessons
                .Include(l => l.Course)
                .FirstOrDefaultAsync(l => l.Id == lessonId && l.Course!.UserId == userId);
            if (lesson == null) return Results.NotFound();

            var existing = await db.UserLessonProgresses
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId);

            int xpEarned = score >= 70 ? lesson.XpReward : (int)(lesson.XpReward * score / 100.0);

            if (existing != null)
            {
                if (score > existing.Score)
                {
                    var bonus = Math.Max(0, xpEarned - existing.XpEarned);
                    existing.Score = score;
                    existing.XpEarned = xpEarned;
                    existing.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    var total2 = await db.UserLessonProgresses.Where(p => p.UserId == userId).SumAsync(p => p.XpEarned);
                    return Results.Json(new { xpEarned = bonus, totalXp = total2, level = XpToLevel(total2), newBest = true });
                }
                var t2 = await db.UserLessonProgresses.Where(p => p.UserId == userId).SumAsync(p => p.XpEarned);
                return Results.Json(new { xpEarned = 0, totalXp = t2, level = XpToLevel(t2), newBest = false });
            }

            db.UserLessonProgresses.Add(new UserLessonProgress
            {
                UserId = userId,
                LessonId = lessonId,
                Score = score,
                XpEarned = xpEarned,
                CompletedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var totalXp = await db.UserLessonProgresses.Where(p => p.UserId == userId).SumAsync(p => p.XpEarned);
            return Results.Json(new { xpEarned, totalXp, level = XpToLevel(totalXp), newBest = true });
        }).DisableAntiforgery();

        // POST /api/courses/generate  — AI generates English course content
        group.MapPost("/generate", async (HttpContext context, ClaimsPrincipal user, AppDbContext db, ICliExecutorService cli) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            async Task Send(string evt, string data)
            {
                await context.Response.WriteAsync($"event: {evt}\ndata: {data}\n\n");
                await context.Response.Body.FlushAsync();
            }

            await Send("status", "AIが英語コースを生成中...");

            var existingCount = await db.Courses.CountAsync(c => c.UserId == userId);
            var startLevel = existingCount == 0 ? "absolute beginner" : $"level {existingCount + 1}";

            var prompt = $@"You are an English language curriculum designer. Generate a structured English learning course in JSON format.

Target level: {startLevel}
Course number: {existingCount + 1}

Return ONLY valid JSON (no markdown, no explanation) with this exact structure:
{{
  ""title"": ""Basics {existingCount + 1}"",
  ""description"": ""Brief course description"",
  ""iconEmoji"": ""🌟"",
  ""color"": ""#58cc02"",
  ""lessons"": [
    {{
      ""title"": ""Lesson title"",
      ""exercises"": [
        {{
          ""type"": ""multiple_choice"",
          ""question"": ""Question text"",
          ""answer"": ""Correct answer"",
          ""options"": [""Option A"", ""Option B"", ""Option C"", ""Option D""],
          ""hint"": ""Optional hint""
        }},
        {{
          ""type"": ""translate"",
          ""question"": ""Translate: [English sentence]"",
          ""answer"": ""Japanese translation"",
          ""hint"": ""Optional hint""
        }},
        {{
          ""type"": ""fill_blank"",
          ""question"": ""The ___ is on the table."",
          ""answer"": ""book"",
          ""options"": [""book"", ""cat"", ""run"", ""happy""],
          ""hint"": ""A common noun""
        }}
      ]
    }}
  ]
}}

Requirements:
- Create exactly 4 lessons for this course
- Each lesson has 5-7 exercises
- Mix exercise types: multiple_choice, translate, fill_blank
- For beginners: greetings, numbers, colors, common nouns
- Questions in Japanese, answers in English (or vice versa for translate type)
- Make it fun and educational like Duolingo
- Return ONLY the JSON, nothing else";

            try
            {
                await Send("status", "AIがカリキュラムを設計中");
                var executeTask = cli.ExecuteAsync(prompt, "gemini");
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(600));
                if (await Task.WhenAny(executeTask, timeoutTask) == timeoutTask)
                    throw new TimeoutException("AI生成がタイムアウトしました (10分)");
                var result = await executeTask;
                var raw = result.Output?.Trim() ?? "";

                if (string.IsNullOrEmpty(raw))
                    throw new InvalidOperationException("AIから空の応答が返されました");

                await Send("status", "JSONを解析中");

                // Extract JSON from possible markdown
                var jsonStart = raw.IndexOf('{');
                var jsonEnd = raw.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                    raw = raw[jsonStart..(jsonEnd + 1)];

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                await Send("status", "コースをデータベースに保存中");

                var course = new Course
                {
                    UserId = userId,
                    Title = root.GetProperty("title").GetString() ?? $"Course {existingCount + 1}",
                    Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                    IconEmoji = root.TryGetProperty("iconEmoji", out var e) ? e.GetString() ?? "📚" : "📚",
                    Color = root.TryGetProperty("color", out var col) ? col.GetString() ?? "#58cc02" : "#58cc02",
                    Order = existingCount,
                    CreatedAt = DateTime.UtcNow
                };
                db.Courses.Add(course);
                await db.SaveChangesAsync();

                if (root.TryGetProperty("lessons", out var lessons))
                {
                    int order = 0;
                    foreach (var l in lessons.EnumerateArray())
                    {
                        var exercises = l.TryGetProperty("exercises", out var ex) ? ex.GetRawText() : "[]";
                        db.Lessons.Add(new Lesson
                        {
                            CourseId = course.Id,
                            Title = l.TryGetProperty("title", out var t) ? t.GetString() ?? $"Lesson {order + 1}" : $"Lesson {order + 1}",
                            Order = order++,
                            ExercisesJson = exercises,
                            XpReward = 10
                        });
                    }
                    await db.SaveChangesAsync();
                }

                await Send("done", JsonSerializer.Serialize(new { courseId = course.Id, title = course.Title }));
            }
            catch (TimeoutException tex)
            {
                await Send("error", JsonSerializer.Serialize(new { message = tex.Message }));
            }
            catch (Exception ex)
            {
                await Send("error", JsonSerializer.Serialize(new { message = ex.Message }));
            }
        }).DisableAntiforgery();

        // DELETE /api/courses/{id}
        group.MapDelete("/{id}", async (int id, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var course = await db.Courses.Include(c => c.Lessons).FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (course == null) return Results.NotFound();
            var lessonIds = course.Lessons.Select(l => l.Id).ToList();
            var progresses = await db.UserLessonProgresses.Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId)).ToListAsync();
            db.UserLessonProgresses.RemoveRange(progresses);
            db.Lessons.RemoveRange(course.Lessons);
            db.Courses.Remove(course);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    private static int XpToLevel(int xp) => xp switch
    {
        < 50 => 1,
        < 150 => 2,
        < 350 => 3,
        < 700 => 4,
        < 1200 => 5,
        < 2000 => 6,
        < 3000 => 7,
        < 5000 => 8,
        _ => 9
    };

    private static async Task<int> GetStreakAsync(AppDbContext db, int userId)
    {
        var dates = await db.UserLessonProgresses
            .Where(p => p.UserId == userId)
            .Select(p => p.CompletedAt.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync();

        if (dates.Count == 0) return 0;
        var streak = 0;
        var today = DateTime.UtcNow.Date;
        var expected = today;
        if (dates[0] < today.AddDays(-1)) return 0;
        foreach (var d in dates)
        {
            if (d == expected || d == expected.AddDays(1)) { streak++; expected = d.AddDays(-1); }
            else break;
        }
        return streak;
    }
}
