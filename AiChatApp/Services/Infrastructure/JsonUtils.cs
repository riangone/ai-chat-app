namespace AiChatApp.Services.Infrastructure;

public static class JsonUtils
{
    public static string ExtractJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return "{}";
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return "{}";
    }
}
