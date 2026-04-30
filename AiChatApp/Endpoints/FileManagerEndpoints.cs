using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AiChatApp.Endpoints;

public static class FileManagerEndpoints
{
    public static void MapFileManagerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/files").RequireAuthorization();

        // GET /api/files?path=/home/user/...&root=/path/to/root → list directory contents as HTML
        group.MapGet("/", async (HttpContext ctx, [FromQuery] string? path, [FromQuery] string? root, [FromQuery] int? page, [FromQuery] int? pageSize) =>
        {
            var userId = int.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var safePath = GetSafePath(path, root);
            if (!Directory.Exists(safePath))
                return Results.Content(BuildErrorHtml($"Directory not found: {safePath}"), "text/html");

            var p = page ?? 1;
            var ps = pageSize ?? 20;

            var entries = new List<FileEntry>();
            try
            {
                foreach (var dir in Directory.GetDirectories(safePath))
                {
                    var info = new DirectoryInfo(dir);
                    entries.Add(new FileEntry
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        IsDirectory = true,
                        Size = 0,
                        ModifiedAt = info.LastWriteTime
                    });
                }
                foreach (var file in Directory.GetFiles(safePath))
                {
                    var info = new FileInfo(file);
                    entries.Add(new FileEntry
                    {
                        Name = info.Name,
                        FullPath = info.FullName,
                        IsDirectory = false,
                        Size = info.Length,
                        ModifiedAt = info.LastWriteTime
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Content(BuildErrorHtml("Access denied"), "text/html");
            }

            var sortedEntries = entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name).ToList();
            var paginatedEntries = sortedEntries.Skip((p - 1) * ps).Take(ps + 1).ToList();
            
            var hasMore = paginatedEntries.Count > ps;
            var entriesToReturn = paginatedEntries.Take(ps).ToList();

            return Results.Content(BuildFileListHtml(entriesToReturn, safePath, root, p, ps, hasMore), "text/html");
        });

        // GET /api/files/content?path=...&root=... → read file content as HTML
        group.MapGet("/content", async (HttpContext ctx, [FromQuery] string? path, [FromQuery] string? root) =>
        {
            var safePath = GetSafePath(path, root);
            if (!System.IO.File.Exists(safePath))
                return Results.Content(BuildErrorHtml("File not found"), "text/html");

            try
            {
                var content = await System.IO.File.ReadAllTextAsync(safePath);
                var ext = Path.GetExtension(safePath).ToLowerInvariant();
                var language = GetLanguageFromExtension(ext);
                return Results.Content(BuildFileContentHtml(safePath, content, language, root), "text/html");
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Content(BuildErrorHtml("Access denied"), "text/html");
            }
        });

        // POST /api/files → create file or directory
        group.MapPost("/", async (HttpContext ctx, [FromForm] string path, [FromForm] string? root, [FromForm] string? content, [FromForm] bool isDirectory = false) =>
        {
            var safePath = GetSafePath(path, root);
            try
            {
                if (isDirectory)
                {
                    Directory.CreateDirectory(safePath);
                }
                else
                {
                    var dir = Path.GetDirectoryName(safePath)!;
                    Directory.CreateDirectory(dir);
                    await System.IO.File.WriteAllTextAsync(safePath, content ?? "");
                }
                return Results.Ok("Created");
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Content(BuildErrorHtml("Access denied"), "text/html");
            }
        });

        // DELETE /api/files?path=...&root=... → delete file or directory
        group.MapDelete("/", async (HttpContext ctx, [FromQuery] string? path, [FromQuery] string? root) =>
        {
            var safePath = GetSafePath(path, root);
            try
            {
                if (Directory.Exists(safePath))
                    Directory.Delete(safePath, true);
                else if (System.IO.File.Exists(safePath))
                    System.IO.File.Delete(safePath);
                return Results.Ok("Deleted");
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Content(BuildErrorHtml("Access denied"), "text/html");
            }
        });
    }

    private static string GetSafePath(string? path, string? root)
    {
        var baseDir = !string.IsNullOrWhiteSpace(root) ? root : AppContext.BaseDirectory;
        
        if (string.IsNullOrWhiteSpace(path))
            return baseDir;

        // If path is absolute and we are using a custom root, handle it
        if (Path.IsPathRooted(path))
        {
            // If it's absolute, check if it starts with baseDir to stay safe
            if (path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return path;
            // Otherwise, we might want to allow it if we trust the user, 
            // but for safety let's combine it if it's not starting with baseDir
            return Path.Combine(baseDir, path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var fullPath = Path.GetFullPath(Path.Combine(baseDir, path));
        // Only enforce startsWith if we are in the default baseDir or if we want strict mode.
        // For now, let's allow browsing anywhere if a custom root is provided, 
        // but still prevent ../ traversal outside of that root.
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            return baseDir;

        return fullPath;
    }

    private static string GetLanguageFromExtension(string ext) => ext switch
    {
        ".cs" => "csharp",
        ".js" => "javascript",
        ".ts" => "typescript",
        ".html" => "html",
        ".css" => "css",
        ".json" => "json",
        ".xml" => "xml",
        ".md" => "markdown",
        ".py" => "python",
        ".java" => "java",
        ".c" or ".cpp" or ".h" => "cpp",
        ".sh" or ".bash" => "bash",
        _ => "plaintext"
    };

    private static string BuildFileListHtml(List<FileEntry> entries, string currentPath, string? root, int page, int pageSize, bool hasMore)
    {
        var baseDir = !string.IsNullOrWhiteSpace(root) ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : AppContext.BaseDirectory;
        var parentPath = Directory.GetParent(currentPath)?.FullName ?? currentPath;
        
        // Ensure parent doesn't go above baseDir
        if (!parentPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            parentPath = baseDir;

        var relPath = Path.GetRelativePath(baseDir, currentPath).Replace('\\', '/');
        if (relPath == ".") relPath = "";

        var sb = new StringBuilder();
        
        // Only render header and breadcrumbs on page 1
        if (page == 1)
        {
            sb.Append($"""
                <div class="p-4 border-b border-base-content/5 space-y-3 bg-base-100/50 backdrop-blur">
                    <div class="flex items-center justify-between">
                        <div class="flex items-center gap-2">
                            <div class="w-8 h-8 rounded-xl bg-primary/10 text-primary flex items-center justify-center">
                                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4"><path stroke-linecap="round" stroke-linejoin="round" d="M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-19.5 0A2.25 2.25 0 0 0 4.5 15h15a2.25 2.25 0 0 0 2.25-2.25m-19.5 0v.243a2.25 2.25 0 0 1 1.5 2.121v.001a2.25 2.25 0 0 0 2.25 2.25h15a2.25 2.25 0 0 0 2.25-2.25v-.001a2.25 2.25 0 0 1 1.5-2.121V12.75" /></svg>
                            </div>
                            <div>
                                <h3 class="text-[10px] font-black uppercase tracking-[0.2em] opacity-40" data-i18n="files">File Manager</h3>
                                <div class="text-[9px] opacity-20 font-mono truncate max-w-[150px] md:max-w-xs">{baseDir}</div>
                            </div>
                        </div>
                        <div class="flex gap-2">
                            <button onclick="showRootSettings()" class="btn btn-ghost btn-sm btn-circle" title="Set Root Directory">
                                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" class="w-5 h-5 opacity-50"><path stroke-linecap="round" stroke-linejoin="round" d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.324.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.24-.438.613-.431.992a6.759 6.759 0 0 1 0 .255c-.007.378.138.75.43.99l1.005.828c.424.35.534.954.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.57 6.57 0 0 1-.22.127c-.332.183-.582.495-.644.869l-.213 1.28c-.09.543-.56.941-1.11.941h-2.594c-.55 0-1.02-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.222-.127c-.324-.196-.72-.257-1.075-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.992a6.932 6.932 0 0 1 0-.255c.007-.378-.138-.75-.43-.99l-1.004-.828a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.087.22-.128.332-.183.582-.495.644-.869l.214-1.281Z" /><path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" /></svg>
                            </button>
                            <button onclick="createFileOrDir()" class="btn btn-primary btn-sm rounded-xl px-4 shadow-lg shadow-primary/20 border-none">
                                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2.5" stroke="currentColor" class="w-4 h-4 mr-1.5"><path stroke-linecap="round" stroke-linejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
                                <span data-i18n="new">New</span>
                            </button>
                        </div>
                    </div>
                    <div class="flex items-center gap-1 text-[11px] font-mono bg-base-300/30 rounded-xl px-3 py-2.5 overflow-x-auto custom-scrollbar no-scrollbar border border-base-content/5">
                        <button onclick="browsePath('')" class="whitespace-nowrap hover:text-primary transition-colors flex items-center gap-1.5 px-1">
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3 h-3 opacity-40"><path stroke-linecap="round" stroke-linejoin="round" d="m2.25 12 8.954-8.955c.44-.439 1.152-.439 1.591 0L21.75 12M4.5 9.75v10.125c0 .621.504 1.125 1.125 1.125H9.75v-4.875c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21h4.125c.621 0 1.125-.504 1.125-1.125V9.75M8.25 21h8.25" /></svg>
                            <span data-i18n="root">root</span>
                        </button>
            """);

            var parts = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var accumulated = "";
            foreach (var part in parts)
            {
                accumulated += (accumulated.Length > 0 ? "/" : "") + part;
                var p = accumulated;
                sb.Append($"""<span class="opacity-20 text-xs">/</span><button onclick="browsePath('{p}')" class="whitespace-nowrap hover:text-primary transition-colors px-1">{part}</button>""");
            }

            sb.Append("""
                    </div>
                </div>
                <div class="flex-1 overflow-y-auto p-3 custom-scrollbar">
                    <div class="max-w-4xl mx-auto space-y-1">
            """);

            // Parent directory link
            if (!string.IsNullOrEmpty(relPath))
            {
                var parentRel = Path.GetRelativePath(baseDir, parentPath).Replace('\\', '/');
                if (parentRel == ".") parentRel = "";
                sb.Append($"""
                        <button onclick="browsePath('{parentRel}')" class="w-full flex items-center gap-3 p-3.5 rounded-2xl hover:bg-base-100 transition-all text-left group border border-transparent hover:border-base-content/5">
                            <div class="w-9 h-9 rounded-xl bg-base-300/50 flex items-center justify-center group-hover:bg-base-300 transition-colors">
                                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2.5" stroke="currentColor" class="w-4 h-4 opacity-40 group-hover:opacity-70"><path stroke-linecap="round" stroke-linejoin="round" d="M6.75 15.75 3 12m0 0 3.75-3.75M3 12h12" /></svg>
                            </div>
                            <span class="text-[11px] font-black uppercase tracking-widest opacity-30 group-hover:opacity-60">.. / (Parent Directory)</span>
                        </button>
                """);
            }
        }

        foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
        {
            var isLast = index == entries.Count - 1 && hasMore;
            var scrollAttr = isLast ? $"hx-get='/api/files?path={Uri.EscapeDataString(relPath)}&root={Uri.EscapeDataString(root ?? "")}&page={page + 1}&pageSize={pageSize}' hx-trigger='revealed' hx-swap='afterend'" : "";

            var relEntryPath = Path.GetRelativePath(baseDir, entry.FullPath).Replace('\\', '/');
            var icon = entry.IsDirectory 
                ? """<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-5 h-5 text-primary"><path stroke-linecap="round" stroke-linejoin="round" d="M2.25 12.75V12A2.25 2.25 0 0 1 4.5 9.75h15A2.25 2.25 0 0 1 21.75 12v.75m-19.5 0A2.25 2.25 0 0 0 4.5 15h15a2.25 2.25 0 0 0 2.25-2.25m-19.5 0v.243a2.25 2.25 0 0 1 1.5 2.121v.001a2.25 2.25 0 0 0 2.25 2.25h15a2.25 2.25 0 0 0 2.25-2.25v-.001a2.25 2.25 0 0 1 1.5-2.121V12.75" /></svg>"""
                : """<svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-5 h-5 text-secondary"><path stroke-linecap="round" stroke-linejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.875v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" /></svg>""";
            
            var bgClass = entry.IsDirectory ? "bg-primary/5 group-hover:bg-primary/10" : "bg-secondary/5 group-hover:bg-secondary/10";
            var clickFn = entry.IsDirectory ? $"browsePath('{relEntryPath}')" : $"viewFile('{relEntryPath}')";
            var infoText = entry.IsDirectory ? $"{entry.ModifiedAt:yyyy-MM-dd HH:mm}" : $"{(entry.Size < 1024 ? $"{entry.Size} B" : entry.Size < 1024 * 1024 ? $"{entry.Size / 1024:F1} KB" : $"{entry.Size / (1024 * 1024):F1} MB")} · {entry.ModifiedAt:yyyy-MM-dd HH:mm}";

            sb.Append($"""
                <div class="group relative flex items-center gap-3 p-3 rounded-2xl hover:bg-base-100 transition-all border border-transparent hover:border-base-content/5" {scrollAttr}>
                    <button onclick="{clickFn}" class="flex-1 flex items-center gap-3 text-left min-w-0">
                        <div class="w-10 h-10 rounded-xl {bgClass} flex items-center justify-center transition-colors">
                            {icon}
                        </div>
                        <div class="flex-1 min-w-0">
                            <div class="text-[13px] font-bold truncate group-hover:text-primary transition-colors">{entry.Name}</div>
                            <div class="text-[10px] opacity-30 font-mono tracking-tight">{infoText}</div>
                        </div>
                    </button>
                    
                    <div class="dropdown dropdown-left dropdown-end opacity-0 group-hover:opacity-100 transition-opacity">
                        <div tabindex="0" role="button" class="btn btn-ghost btn-xs btn-circle">
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-4 h-4 opacity-40"><path stroke-linecap="round" stroke-linejoin="round" d="M12 6.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5ZM12 12.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5ZM12 18.75a.75.75 0 1 1 0-1.5.75.75 0 0 1 0 1.5Z" /></svg>
                        </div>
                        <ul tabindex="0" class="dropdown-content z-[110] menu p-2 shadow-xl bg-base-300 rounded-xl w-32 border border-base-content/5 mt-1">
                            <li><a onclick="deleteEntry('{relEntryPath}', {entry.IsDirectory.ToString().ToLower()})" class="text-error text-xs font-bold py-2"><svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3.5 h-3.5"><path stroke-linecap="round" stroke-linejoin="round" d="m14.74 9-.34 12m-4.72 0-.34-12M4.5 18.75A2.25 2.25 0 0 0 6.75 21h10.5a2.25 2.25 0 0 0 2.25-2.25V6.75m19.5 0A2.25 2.25 0 0 0 19.5 4.5h-15a2.25 2.25 0 0 0-2.25 2.25V6.75m19.5 0v.243a2.25 2.25 0 0 1-1.5 2.121v.001a2.25 2.25 0 0 0 2.25 2.25h15a2.25 2.25 0 0 0 2.25-2.25v-.001a2.25 2.25 0 0 1 1.5-2.121V6.75" /></svg><span data-i18n="delete">Delete</span></a></li>
                        </ul>
                    </div>
                </div>
            """);
        }

        if (entries.Count == 0 && string.IsNullOrEmpty(relPath) && page == 1)
            sb.Append("""<div class="flex flex-col items-center justify-center py-20 opacity-20"><div class="w-16 h-16 border-2 border-dashed border-current rounded-full mb-4 animate-pulse"></div><p class="text-xs font-black uppercase tracking-widest">Empty directory</p></div>""");

        if (page == 1)
        {
            sb.Append("""
                    </div>
                </div>
            """);
        }

        return sb.ToString();
    }

    private static string BuildFileContentHtml(string fullPath, string content, string language, string? root)
    {
        var baseDir = !string.IsNullOrWhiteSpace(root) ? root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : AppContext.BaseDirectory;
        var fileName = Path.GetFileName(fullPath);
        var relPath = Path.GetRelativePath(baseDir, fullPath).Replace('\\', '/');
        var parentDir = Path.GetRelativePath(baseDir, Path.GetDirectoryName(fullPath)!).Replace('\\', '/');
        if (parentDir == ".") parentDir = "";

        return $"""
            <div class="flex flex-col h-full bg-base-200/50">
                <div class="p-4 border-b border-base-content/5 space-y-4 bg-base-100/50 backdrop-blur">
                    <div class="flex items-center justify-between">
                        <div class="flex items-center gap-2">
                            <button onclick="browsePath('{parentDir}')" class="btn btn-ghost btn-sm gap-2 rounded-xl px-3">
                                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2.5" stroke="currentColor" class="w-4 h-4"><path stroke-linecap="round" stroke-linejoin="round" d="M6.75 15.75 3 12m0 0 3.75-3.75M3 12h12" /></svg>
                                <span class="text-xs font-bold" data-i18n="back">Back</span>
                            </button>
                        </div>
                        <div class="flex items-center gap-3">
                            <div class="badge badge-outline opacity-40 font-mono text-[10px] py-3 tracking-widest uppercase">{language}</div>
                            <button onclick="navigator.clipboard.writeText('{relPath}'); alert('Path copied!');" class="btn btn-ghost btn-xs btn-circle opacity-40 hover:opacity-100">
                                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-3.5 h-3.5"><path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25c0-4.46-3.243-8.161-7.5-8.876a9.06 9.06 0 0 0-1.5-.124H9.375c-.621 0-1.125.504-1.125 1.125v3.5m7.5 10.375H9.375a1.125 1.125 0 0 1-1.125-1.125v-9.25m12 6.625v-1.875a3.375 3.375 0 0 0-3.375-3.375h-1.5a1.125 1.125 0 0 1-1.125-1.125v-1.5a3.375 3.375 0 0 0-3.375-3.375H9.75" /></svg>
                            </button>
                        </div>
                    </div>
                    <div class="flex items-center gap-3">
                        <div class="w-10 h-10 rounded-xl bg-secondary/10 text-secondary flex items-center justify-center">
                            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor" class="w-5 h-5"><path stroke-linecap="round" stroke-linejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 0 0-3.375-3.375h-1.5A1.125 1.125 0 0 1 13.5 7.875v-1.5a3.375 3.375 0 0 0-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 0 0-9-9Z" /></svg>
                        </div>
                        <div class="min-w-0">
                            <h3 class="text-sm font-black truncate">{fileName}</h3>
                            <div class="text-[10px] opacity-20 font-mono truncate">{relPath}</div>
                        </div>
                    </div>
                </div>
                <div class="flex-1 overflow-y-auto p-4 md:p-6 custom-scrollbar">
                    <div class="max-w-5xl mx-auto">
                        <pre class="text-[11px] font-mono whitespace-pre-wrap overflow-x-auto bg-base-300/30 p-5 rounded-2xl border border-base-content/5 shadow-inner leading-relaxed">{System.Net.WebUtility.HtmlEncode(content)}</pre>
                    </div>
                </div>
            </div>
        """;
    }

    private static string BuildErrorHtml(string message)
    {
        return $"""
            <div class="flex flex-col items-center justify-center h-full opacity-40 space-y-4">
                <div class="w-16 h-16 border-4 border-dashed border-current rounded-full opacity-20"></div>
                <p class="text-sm font-medium">{message}</p>
            </div>
        """;
    }

    private class FileEntry
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime ModifiedAt { get; set; }
    }
}
