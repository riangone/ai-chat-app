using System.Net;
using AiChatApp.Models;

namespace AiChatApp.Services.Infrastructure;

public static class HtmlUtils
{
    public static string RenderMessage(Message m, List<AgentStep>? steps = null)
    {
        var promptT = steps?.Sum(s => s.PromptTokens) ?? 0;
        var completionT = steps?.Sum(s => s.CompletionTokens) ?? 0;
        var totalT = steps?.Sum(s => s.TotalTokens) ?? 0;
        if (totalT == 0) totalT = promptT + completionT;
        var tokenHtml = m.IsAi && totalT > 0
            ? $"<span class='text-[10px] opacity-30 font-mono'>↑{promptT:N0} ↓{completionT:N0}</span>"
            : "";

        var agentBadge = (m.IsAi && !string.IsNullOrEmpty(m.AgentName))
            ? $"<span class='badge badge-ghost badge-xs opacity-50 font-bold uppercase tracking-wider'>{m.AgentName}</span>"
            : "";

        var multiAgentLabel = (steps != null && steps.Any())
            ? "<span class='ml-auto text-[10px] opacity-30 font-semibold'>MULTI-AGENT</span>"
            : "";

        return $@"
        <div class='chat {(m.IsAi ? "chat-start" : "chat-end")} group message-bubble-container'>
            <div class='chat-bubble shadow-sm {(m.IsAi ? "bg-base-200 text-base-content border border-base-300" : "bg-primary text-primary-content")} markdown leading-relaxed p-3 md:p-4 rounded-[18px] {(m.IsAi ? "rounded-bl-none" : "rounded-tr-none")}'>
                <div class='content-body'>{WebUtility.HtmlEncode(m.Content)}</div>
            </div>
            <div class='chat-footer flex items-center gap-3 pt-1 px-1'>
                {agentBadge}
                <time class='text-[10px] opacity-40 font-mono'>{m.Timestamp.ToLocalTime().ToString("yyyy/MM/dd HH:mm")}</time>
                {tokenHtml}
                <div class='opacity-0 group-hover:opacity-100 transition-opacity flex gap-3'>
                    <button class='hover:text-primary transition-colors' onclick='copyText(this)' title='Copy'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 0 1-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 0 1 1.5 1.5h6.375a1.125 1.125 0 0 1 1.125 1.125v9.375Zm3 3V6.75a1.125 1.125 0 0 0-1.125-1.125h-1.5a3.375 3.375 0 0 1-3.375-3.375V2.125c0-.621-.504-1.125-1.125-1.125H9.75a1.125 1.125 0 0 0-1.125 1.125V4.5a9.06 9.06 0 0 1 1.5 1.5h6.75a1.125 1.125 0 0 1 1.125 1.125v13.125a1.125 1.125 0 0 1-1.125 1.125H15"" /></svg>
                    </button>
                    <button class='hover:text-primary transition-colors' onclick='forwardText(this)' title='Forward'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M9 15 3 9m0 0 6-6M3 9h12a6 6 0 0 1 0 12h-3"" /></svg>
                    </button>
                    {(m.IsAi ? $@"<button class='hover:text-primary transition-colors' onclick='saveToMemory(this)' title='Save to Memory'>
                        <svg xmlns=""http://www.w3.org/2000/svg"" fill=""none"" viewBox=""0 0 24 24"" stroke-width=""1.5"" stroke=""currentColor"" class=""w-3.5 h-3.5""><path stroke-linecap=""round"" stroke-linejoin=""round"" d=""M12 18.75a6 6 0 0 0 6-6v-1.5m-6 7.5a6 6 0 0 1-6-6v-1.5m6 7.5v3.75m-3.75 0h7.5M12 15.75a3 3 0 0 1-3-3V4.5a3 3 0 1 1 6 0v8.25a3 3 0 0 1-3 3Z"" /></svg>
                    </button>" : "")}
                </div>
                {multiAgentLabel}
            </div>
        </div>";
    }

    public static string RenderLoadMoreButton(int sessionId, int oldestId)
    {
        return $@"
        <div id='load-more-container' class='flex justify-center py-4'>
            <button class='btn btn-ghost btn-xs opacity-50 hover:opacity-100'
                    hx-get='/api/chat/{sessionId}/older-messages?beforeId={oldestId}'
                    hx-target='#load-more-container'
                    hx-swap='outerHTML'>
                Load Older Messages...
            </button>
        </div>";
    }
}
