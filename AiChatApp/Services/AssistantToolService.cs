using System.Text.Json;
using System.Text.RegularExpressions;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiChatApp.Services;

public record ToolCallResult(string ToolName, bool Success, string Message, string? HtmlBadge = null);

public class AssistantToolService
{
    private readonly AppDbContext _db;
    private readonly ProactiveBrainService _brain;
    private readonly ILogger<AssistantToolService> _logger;

    private static readonly Regex ToolCallRegex = new(@"<tool_call>(.*?)</tool_call>", RegexOptions.Singleline | RegexOptions.Compiled);

    public AssistantToolService(AppDbContext db, ProactiveBrainService brain, ILogger<AssistantToolService> logger)
    {
        _db = db;
        _brain = brain;
        _logger = logger;
    }

    public (string CleanResponse, List<ToolCallResult> Results) ExtractAndStrip(string response)
    {
        var results = new List<ToolCallResult>();
        var clean = ToolCallRegex.Replace(response, m =>
        {
            results.Add(new ToolCallResult(m.Value, false, "pending"));
            return "";
        });
        results.Clear();
        var toolCalls = ToolCallRegex.Matches(response);
        foreach (Match match in toolCalls)
        {
            try
            {
                var json = match.Groups[1].Value.Trim();
                var doc = JsonDocument.Parse(json);
                var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                var args = doc.RootElement.TryGetProperty("args", out var a) ? a : default;
                results.Add(new ToolCallResult(name, false, json));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse tool_call: {Raw}", match.Value);
            }
        }
        return (ToolCallRegex.Replace(response, "").Trim(), results);
    }

    public async Task<List<ToolCallResult>> ExecuteToolCallsAsync(string response, int userId)
    {
        var results = new List<ToolCallResult>();
        var toolCalls = ToolCallRegex.Matches(response);

        foreach (Match match in toolCalls)
        {
            try
            {
                var json = match.Groups[1].Value.Trim();
                var doc = JsonDocument.Parse(json);
                var name = doc.RootElement.GetProperty("name").GetString() ?? "";
                var args = doc.RootElement.TryGetProperty("args", out var a) ? a : default;

                var result = name switch
                {
                    "create_todo" => await CreateTodoAsync(args, userId),
                    "save_note" => await SaveNoteAsync(args, userId),
                    "save_memory" => await SaveMemoryAsync(args, userId),
                    _ => new ToolCallResult(name, false, $"Unknown tool: {name}")
                };
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool call execution failed for: {Raw}", match.Value);
                results.Add(new ToolCallResult("unknown", false, $"Error: {ex.Message}"));
            }
        }

        return results;
    }

    private async Task<ToolCallResult> CreateTodoAsync(JsonElement args, int userId)
    {
        var title = args.TryGetProperty("title", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(title))
            return new ToolCallResult("create_todo", false, "Missing title");

        var dueDate = args.TryGetProperty("due_date", out var dd) ? dd.GetString() : null;
        DateTime? parsedDue = null;
        if (!string.IsNullOrEmpty(dueDate) && DateTime.TryParse(dueDate, out var d)) parsedDue = d.ToUniversalTime();

        var item = new TodoItem
        {
            Title = title,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            DueDate = parsedDue
        };
        _db.TodoItems.Add(item);
        await _db.SaveChangesAsync();
        _brain.ProcessTodoChange(item, "created");

        var badge = $"<div class=\"tool-action-badge\"><span class=\"badge badge-success gap-1\">✓ タスク作成: {System.Net.WebUtility.HtmlEncode(title)}</span></div>";
        return new ToolCallResult("create_todo", true, $"Created todo: {title}", badge);
    }

    private async Task<ToolCallResult> SaveNoteAsync(JsonElement args, int userId)
    {
        var title = args.TryGetProperty("title", out var t) ? t.GetString() : "AI Note";
        var content = args.TryGetProperty("content", out var c) ? c.GetString() : "";

        var note = new Note
        {
            Title = title ?? "AI Note",
            Content = content ?? "",
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync();

        var badge = $"<div class=\"tool-action-badge\"><span class=\"badge badge-info gap-1\">✓ ノート保存: {System.Net.WebUtility.HtmlEncode(title)}</span></div>";
        return new ToolCallResult("save_note", true, $"Saved note: {title}", badge);
    }

    private async Task<ToolCallResult> SaveMemoryAsync(JsonElement args, int userId)
    {
        var content = args.TryGetProperty("content", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(content))
            return new ToolCallResult("save_memory", false, "Missing content");

        var tags = args.TryGetProperty("tags", out var tg) ? tg.GetString() : "";
        var tagList = string.IsNullOrEmpty(tags) ? new List<string>() : tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

        LongTermMemory? existing = null;
        if (tagList.Any())
        {
            existing = await _db.LongTermMemories
                .Where(m => m.UserId == userId && m.Tags == string.Join(",", tagList))
                .FirstOrDefaultAsync();
        }

        if (existing != null)
        {
            existing.Content = content;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var memory = new LongTermMemory
            {
                Content = content,
                Tags = string.Join(",", tagList),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.LongTermMemories.Add(memory);
        }
        await _db.SaveChangesAsync();

        var badge = $"<div class=\"tool-action-badge\"><span class=\"badge badge-warning gap-1\">✓ 記憶保存完了</span></div>";
        return new ToolCallResult("save_memory", true, "Memory saved", badge);
    }

    public static string StripToolCalls(string response) =>
        ToolCallRegex.Replace(response, "").Trim();

    public static string BuildResultsHtml(List<ToolCallResult> results)
    {
        if (!results.Any(r => r.Success)) return "";
        var badges = results.Where(r => r.Success && r.HtmlBadge != null).Select(r => r.HtmlBadge!);
        return string.Join("", badges);
    }
}
