using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.EntityFrameworkCore;

namespace AiChatApp.Services;

public class AttachmentService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".markdown", ".csv", ".log", ".json", ".yaml", ".yml", ".xml",
          ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx", ".vue", ".svelte",
          ".py", ".rb", ".go", ".rs", ".cs", ".java", ".cpp", ".c", ".h", ".hpp",
          ".sh", ".bash", ".zsh", ".fish", ".ps1", ".bat", ".cmd",
          ".sql", ".graphql", ".proto", ".toml", ".ini", ".env", ".conf", ".cfg" };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxTextContentForPrompt = 60_000;      // chars injected into AI prompt

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AttachmentService(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public string GetUploadsDir(int userId)
    {
        var dir = Path.Combine(_env.WebRootPath, "uploads", userId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<(Attachment? Record, string? Error)> SaveAsync(IFormFile file, int userId)
    {
        if (file.Length == 0) return (null, "Empty file.");
        if (file.Length > MaxFileSizeBytes) return (null, $"File too large (max {MaxFileSizeBytes / 1024 / 1024} MB).");

        var ext = Path.GetExtension(file.FileName);
        var safeName = Path.GetFileNameWithoutExtension(file.FileName);
        safeName = string.Concat(safeName.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        if (safeName.Length == 0) safeName = "file";
        if (safeName.Length > 60) safeName = safeName[..60];

        var storedName = $"{Guid.NewGuid():N}_{safeName}{ext}";
        var dir = GetUploadsDir(userId);
        var fullPath = Path.Combine(dir, storedName);

        using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream);

        var fileType = GetFileType(ext);
        var relativePath = $"/uploads/{userId}/{storedName}";

        var attachment = new Attachment
        {
            UserId = userId,
            FileName = file.FileName,
            StoredPath = relativePath,
            FileSize = file.Length,
            MimeType = file.ContentType ?? "application/octet-stream",
            FileType = fileType,
            UploadedAt = DateTime.UtcNow
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync();
        return (attachment, null);
    }

    public async Task<bool> DeleteAsync(int id, int userId)
    {
        var att = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (att == null) return false;

        var fullPath = Path.Combine(_env.WebRootPath, att.StoredPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath)) File.Delete(fullPath);

        _db.Attachments.Remove(att);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task LinkToMessageAsync(IEnumerable<int> attachmentIds, int messageId, int userId)
    {
        var ids = attachmentIds.ToList();
        if (ids.Count == 0) return;
        var attachments = await _db.Attachments
            .Where(a => ids.Contains(a.Id) && a.UserId == userId && a.MessageId == null)
            .ToListAsync();
        foreach (var a in attachments) a.MessageId = messageId;
        await _db.SaveChangesAsync();
    }

    // Builds the context block that gets prepended to the AI prompt
    public async Task<string> BuildPromptContextAsync(IEnumerable<int> attachmentIds, int userId)
    {
        var ids = attachmentIds.ToList();
        if (ids.Count == 0) return "";

        var attachments = await _db.Attachments
            .Where(a => ids.Contains(a.Id) && a.UserId == userId)
            .ToListAsync();

        if (attachments.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n[Attached Files]:");

        foreach (var att in attachments)
        {
            var fullPath = Path.Combine(_env.WebRootPath, att.StoredPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (att.FileType == "image")
            {
                // Use absolute path with @ prefix for CLI image support
                sb.AppendLine($"- Image: @{fullPath}");
            }
            else if (att.FileType == "text" && File.Exists(fullPath))
            {
                var content = await File.ReadAllTextAsync(fullPath);
                if (content.Length > MaxTextContentForPrompt)
                    content = content[..MaxTextContentForPrompt] + "\n... [truncated]";
                var ext = Path.GetExtension(att.FileName).TrimStart('.');
                sb.AppendLine($"- File: {att.FileName}");
                sb.AppendLine($"```{ext}");
                sb.AppendLine(content);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine($"- File: {att.FileName} ({att.MimeType}, {att.FileSize / 1024} KB) [binary, content not available]");
            }
        }

        return sb.ToString();
    }

    private static string GetFileType(string ext)
    {
        if (ImageExtensions.Contains(ext)) return "image";
        if (TextExtensions.Contains(ext)) return "text";
        return "binary";
    }
}
