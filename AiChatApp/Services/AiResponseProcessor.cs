using System.Text;
using AiChatApp.Models;

namespace AiChatApp.Services;

public class AiResponseProcessor
{
    private readonly SessionMemoryService _sessionMemory;

    private static readonly HashSet<string> PromptPrefixSet = new(AiPromptService.PromptPrefixes, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SystemPromptFragmentSet = new(AiPromptService.SystemPromptFragments, StringComparer.OrdinalIgnoreCase);

    public AiResponseProcessor(SessionMemoryService sessionMemory)
    {
        _sessionMemory = sessionMemory;
    }

    public string BuildStepHtml(AgentStep step)
    {
        string badgeClass = step.Role switch { "Orchestrator" => "badge-info", "Executor" => "badge-success", "Reviewer" => "badge-secondary", _ => "badge-ghost" };
        string retryBadge = step.AttemptNumber > 1 ? $"<span class='badge badge-warning badge-xs ml-1'>Retry #{step.AttemptNumber}</span>" : "";
        string acceptedIcon = step.WasAccepted ? "" : "<span class='badge badge-error badge-xs ml-1'>Rejected</span>";
        return $"<div class='collapse collapse-arrow bg-base-300/30 border border-base-content/10 mb-2'><input type='checkbox' /><div class='collapse-title text-sm font-medium flex items-center gap-2'><span class='badge {badgeClass} badge-sm'>{step.Role}</span>{retryBadge}{acceptedIcon}<span class='opacity-50 text-xs ml-auto'>{step.DurationMs}ms</span></div><div class='collapse-content text-xs opacity-80'><pre class='whitespace-pre-wrap font-sans'>{System.Net.WebUtility.HtmlEncode(step.Output)}</pre></div></div>";
    }

    public string BuildCooperativeHtml(List<AgentStep> steps, string finalOutput) => 
        System.Net.WebUtility.HtmlEncode(finalOutput);

    public async Task ParseAndSaveMemoryAsync(int sessionId, string role, string output)
    {
        if (!output.Contains("MEMORY:", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var line in output.Split('\n')) { 
            if (!line.Contains("MEMORY:", StringComparison.OrdinalIgnoreCase)) continue; 
            var content = line[(line.IndexOf("MEMORY:", StringComparison.OrdinalIgnoreCase) + 7)..].Trim(); 
            var parts = content.Split('=', 2); 
            if (parts.Length == 2) await _sessionMemory.WriteAsync(sessionId, role, parts[0].Trim(), parts[1].Trim()); 
        }
    }

    public HashSet<string> BuildDynamicFragments(string? systemPrompt, string? userPrompt = null)
    {
        var dynamicFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(systemPrompt)) foreach (var line in systemPrompt.Split('\n')) { var tl = line.Trim(); if (tl.Length > 3) dynamicFragments.Add(tl); }
        if (!string.IsNullOrEmpty(userPrompt)) foreach (var line in userPrompt.Split('\n')) { var tl = line.Trim(); if (tl.Length > 3) dynamicFragments.Add(tl); }
        return dynamicFragments;
    }

    public (string? toYield, bool handled) HandlePrefixBuffer(StringBuilder prefixBuffer, string chunk, bool prefixHandled, int maxBuffer, HashSet<string>? dynamicFrags = null)
    {
        if (prefixHandled) return (chunk, true);
        prefixBuffer.Append(chunk); var buf = prefixBuffer.ToString();
        bool startsWithPrefix = BufStartsWithAny(buf, PromptPrefixSet) || BufStartsWithAny(buf, SystemPromptFragmentSet) || (dynamicFrags != null && dynamicFrags.Any(f => buf.StartsWith(f, StringComparison.OrdinalIgnoreCase)));
        bool couldBeginPrefix = AnyStartsWithBuf(buf, PromptPrefixSet) || AnyStartsWithBuf(buf, SystemPromptFragmentSet) || (dynamicFrags != null && dynamicFrags.Any(f => f.StartsWith(buf, StringComparison.OrdinalIgnoreCase)));
        
        if (!startsWithPrefix && !couldBeginPrefix) { 
            var stripped = StripEchoedPromptPrefix(buf, dynamicFrags); 
            prefixBuffer.Clear(); 
            return (string.IsNullOrEmpty(stripped) ? null : stripped, true); 
        }
        
        if (startsWithPrefix && (buf.Contains("\nUser:") || buf.Contains("\nAssistant:") || buf.Contains("\nAssistant ") || buf.Contains("\n["))) { 
            var stripped = StripEchoedPromptPrefix(buf, dynamicFrags); 
            prefixBuffer.Clear(); 
            return (string.IsNullOrEmpty(stripped) ? null : stripped, true); 
        }
        
        if (buf.Length >= maxBuffer) { 
            var stripped = StripEchoedPromptPrefix(buf, dynamicFrags); 
            prefixBuffer.Clear(); 
            return (string.IsNullOrEmpty(stripped) ? null : stripped, true); 
        }
        return (null, false);
    }

    public string StripEchoedPromptPrefix(string text, HashSet<string>? dynamicFragments = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Split('\n'); int firstContentLine = -1; string? firstLineContent = null;
        for (int i = 0; i < Math.Min(lines.Length, 50); i++) {
            var trimmedLine = lines[i].Trim(); if (string.IsNullOrWhiteSpace(trimmedLine)) continue;
            string? matchedPrefix = PromptPrefixSet.FirstOrDefault(p => trimmedLine.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            bool isSystemFragment = SystemPromptFragmentSet.Any(f => trimmedLine.Contains(f, StringComparison.OrdinalIgnoreCase)) || (dynamicFragments != null && dynamicFragments.Contains(trimmedLine));
            
            if (matchedPrefix != null) { 
                if (dynamicFragments != null && dynamicFragments.Contains(trimmedLine)) continue; 
                var contentAfter = trimmedLine[matchedPrefix!.Length..].Trim(); 
                if (!string.IsNullOrEmpty(contentAfter)) { 
                    if (SystemPromptFragmentSet.Any(f => contentAfter.Contains(f, StringComparison.OrdinalIgnoreCase)) || (dynamicFragments != null && dynamicFragments.Any(f => contentAfter.Contains(f, StringComparison.OrdinalIgnoreCase)))) continue; 
                    firstContentLine = i; firstLineContent = contentAfter; break; 
                } continue; 
            }
            if (isSystemFragment) continue;
            firstContentLine = i; firstLineContent = lines[i]; break;
        }
        if (firstContentLine != -1) { 
            var remainingLines = lines.Skip(firstContentLine + 1).ToList(); 
            return (firstLineContent + (remainingLines.Any() ? "\n" + string.Join("\n", remainingLines) : "")).Trim(); 
        }
        return string.Empty;
    }

    private static bool BufStartsWithAny(string buf, HashSet<string> set) => set.Any(p => buf.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    private static bool AnyStartsWithBuf(string buf, HashSet<string> set) => set.Any(p => p.StartsWith(buf, StringComparison.OrdinalIgnoreCase));
}
