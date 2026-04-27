using System.Text;
using System.Text.Json;
using System.Net;

namespace AiChatApp.Endpoints;

public static class CliEndpoints
{
    public static void MapCliEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cli").RequireAuthorization();

        group.MapGet("/sessions", () => {
            var sessions = new List<dynamic>();
            
            // 1. Gemini
            var geminiPath = "/home/ubuntu/.gemini/tmp/ai-chat-app/chats";
            if (Directory.Exists(geminiPath)) {
                sessions.AddRange(Directory.GetFiles(geminiPath, "session-*.json")
                    .Select(f => new FileInfo(f))
                    .Select(f => new { Source = "Gemini", Name = f.Name, Time = f.LastWriteTime, Path = f.FullName }));
            }

            // 2. Claude
            var claudePath = "/home/ubuntu/.cache/claude-cli-nodejs";
            if (Directory.Exists(claudePath)) {
                sessions.AddRange(Directory.GetFiles(claudePath, "*.jsonl")
                    .Select(f => new FileInfo(f))
                    .Select(f => new { Source = "Claude", Name = f.Name, Time = f.LastWriteTime, Path = f.FullName }));
            }

            // 3. Codex
            var codexHistory = "/home/ubuntu/.codex/history.jsonl";
            if (File.Exists(codexHistory)) {
                sessions.Add(new { Source = "Codex", Name = "history.jsonl", Time = new FileInfo(codexHistory).LastWriteTime, Path = codexHistory });
            }

            // 4. Copilot
            var copilotPath = "/home/ubuntu/.copilot/logs";
            if (Directory.Exists(copilotPath)) {
                sessions.AddRange(Directory.GetFiles(copilotPath, "*.log")
                    .Select(f => new FileInfo(f))
                    .Select(f => new { Source = "Copilot", Name = f.Name, Time = f.LastWriteTime, Path = f.FullName }));
            }

            var sorted = sessions.OrderByDescending(s => s.Time).Take(50).ToList();

            return Results.Content(string.Concat(sorted.Select(s => {
                var badgeColor = s.Source switch {
                    "Gemini" => "badge-primary",
                    "Claude" => "badge-secondary",
                    "Codex" => "badge-accent",
                    "Copilot" => "badge-info",
                    _ => "badge-ghost"
                };
                return $@"
                <div class='flex items-center group w-full mb-1'>
                    <button onclick='loadCliSession(""{s.Source}"", ""{s.Path.Replace("\\", "\\\\").Replace("\"", "&quot;")}"")'
                            class='btn btn-ghost btn-sm flex-1 justify-start overflow-hidden text-ellipsis whitespace-nowrap font-normal text-xs'>
                        <span class='badge {badgeColor} badge-xs mr-2 opacity-70'>{s.Source}</span>
                        {s.Time:MM/dd HH:mm}
                    </button>
                </div>";
            })), "text/html");
        });

        group.MapGet("/load", (string source, string path) => {
            if (!File.Exists(path)) return Results.NotFound();

            var htmlBuilder = new StringBuilder();
            htmlBuilder.Append("<div id='chat-box' class='flex-1 overflow-y-auto p-4 md:p-6 space-y-8 custom-scrollbar'>");
            htmlBuilder.Append($@"<div class='alert alert-info shadow-sm mb-4'><svg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 24 24' class='stroke-current shrink-0 w-6 h-6'><path stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z'></path></svg><span>Viewing {source} Session (Read-Only)</span></div>");

            if (source == "Gemini") {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var messages = doc.RootElement.GetProperty("messages");
                foreach (var msg in messages.EnumerateArray()) {
                    var type = msg.GetProperty("type").GetString();
                    var isAi = type == "gemini";
                    string content = isAi ? (msg.GetProperty("content").GetString() ?? "") : 
                        string.Join("\n", msg.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString()));
                    htmlBuilder.Append(RenderCliMessage(content, isAi));
                }
            } 
            else if (source == "Claude" || source == "Codex") {
                var lines = File.ReadLines(path).TakeLast(100);
                foreach (var line in lines) {
                    try {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        string? text = null;

                        if (source == "Codex" && root.TryGetProperty("text", out var t)) {
                            text = t.GetString();
                        } else if (source == "Claude") {
                            if (root.TryGetProperty("content", out var c)) text = c.GetString();
                            else if (root.TryGetProperty("debug", out var d)) text = $"[Debug] {d.GetString()}";
                        }

                        if (!string.IsNullOrEmpty(text)) {
                            htmlBuilder.Append(RenderCliMessage(text, false));
                        }
                    } catch { }
                }
            }
            else {
                htmlBuilder.Append("<p class='p-4 opacity-50'>Log format parsing for this source is coming soon. Showing raw content:</p>");
                htmlBuilder.Append($"<pre class='p-4 bg-base-300 rounded text-xs overflow-auto'>{File.ReadAllText(path)}</pre>");
            }

            htmlBuilder.Append("</div><script>renderMarkdown(); syncActiveSessionFromDom(); scrollToBottom();</script>");
            return Results.Content(htmlBuilder.ToString(), "text/html");
        });
    }

    private static string RenderCliMessage(string content, bool isAi) => $@"
        <div class='chat {(isAi ? "chat-start" : "chat-end")} group message-bubble-container'>
            <div class='chat-bubble shadow-sm {(isAi ? "bg-base-200 text-base-content border border-base-300" : "bg-primary text-primary-content")} markdown leading-relaxed p-3 md:p-4 rounded-[18px] {(isAi ? "rounded-bl-none" : "rounded-tr-none")}'>
                <div class='content-body'>{WebUtility.HtmlEncode(content)}</div>
            </div>
            <div class='chat-footer opacity-0 group-hover:opacity-50 transition-opacity flex gap-3 pt-2 px-1'>
                <button class='hover:text-primary transition-colors' onclick='copyText(this)' title='Copy'>
                    <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5 1.5h6.375a1.125 1.125 0 0 1 1.125 1.125v9.375Zm3 3V6.75a1.125 1.125 0 0 0-1.125-1.125h-1.5a3.375 3.375 0 0 1-3.375-3.375V2.125c0-.621-.504-1.125-1.125-1.125H9.75a1.125 1.125 0 0 0-1.125 1.125V4.5a9.06 9.06 0 0 1 1.5 1.5h6.75a1.125 1.125 0 0 1 1.125 1.125v13.125a1.125 1.125 0 0 1-1.125 1.125H15"" /></svg>
                </button>
                <button class='hover:text-primary transition-colors' onclick='forwardText(this)' title='Forward'>
                    <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 15 3 9m0 0 6-6M3 9h12a6 6 0 0 1 0 12h-3"" /></svg>
                </button>
                {(isAi ? $@"<button class='hover:text-primary transition-colors' onclick='saveToMemory(this)' title='Save to Memory'>
                    <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M12 18.75a6 6 0 0 0 6-6v-1.5m-6 7.5a6 6 0 0 1-6-6v-1.5m6 7.5v3.75m-3.75 0h7.5M12 15.75a3 3 0 0 1-3-3V4.5a3 3 0 1 1 6 0v8.25a3 3 0 0 1-3 3Z"" /></svg>
                </button>" : "")}
            </div>
        </div>";
}
