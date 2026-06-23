namespace AiChatApp.Services.Infrastructure;

public static class ProviderRegistry
{
    public static string NormalizeProvider(string provider, string? model = null)
    {
        var p = provider.ToLower();
        var m = model?.ToLower() ?? "";
        
        if (p.Contains("copilot")) return "Copilot";
        if (p.Contains("antigravity") || p.Contains("gemini") || m.Contains("antigravity") || m.Contains("gemini")) return "Antigravity";
        if (p.Contains("claude") || p.Contains("anthropic") || m.Contains("claude") || m.Contains("anthropic") || m.Contains("sonnet") || m.Contains("haiku") || m.Contains("opus")) return "Claude";
        if (p.Contains("codex") || m.Contains("codex")) return "Codex";
        if (p.Contains("opencode") || p.Contains("open-code") || m.Contains("opencode") || m.Contains("open-code")) return "OpenCode";
        if (p.Contains("deepseek") || m.Contains("deepseek")) return "DeepSeek";
        if (p.Contains("gh-copilot") || m.Contains("gpt-4") && p == "copilot") return "Copilot";
        if (p.Contains("gpt") || p.Contains("openai") || m.Contains("gpt-") || m.Contains("o1-") || m.Contains("o3-")) return "OpenAI";
        if (p.Contains("lmstudio") || p.Contains("lm-studio") || m.Contains("lmstudio")) return "LmStudio";
        
        return "Other";
    }

    public static string GetColorClass(string provider)
    {
        return provider switch {
            "Antigravity" or "Gemini" => "text-blue-400",
            "Claude" => "text-orange-400",
            "Codex" => "text-emerald-400",
            "Copilot" => "text-indigo-400",
            "OpenCode" => "text-yellow-400",
            "OpenAI" => "text-emerald-500",
            "DeepSeek" => "text-cyan-400",
            "LmStudio" => "text-pink-400",
            _ => "text-primary"
        };
    }

    public static readonly string[] AllProviders = { "Antigravity", "Claude", "Codex", "Copilot", "OpenCode", "OpenAI", "DeepSeek", "LmStudio" };

    public static readonly Dictionary<string, long> Quotas = new() {
        { "Antigravity", 10000000 },
        { "Gemini", 10000000 },
        { "Claude", 5000000 },
        { "Codex", 2000000 },
        { "Copilot", 2000000 },
        { "OpenCode", 5000000 },
        { "OpenAI", 2000000 },
        { "DeepSeek", 20000000 },
        { "LmStudio", 100000000 },
        { "Other", 1000000 }
    };
}
