namespace AiChatApp.Services.Infrastructure;

using System.Collections.Generic;
using System.Text;

public static class JsonUtils
{
    public static string ExtractJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return "{}";
        
        // Try to find the last occurrence of a complete JSON object first, 
        // as the final response is usually at the end.
        var objects = ExtractAllJsonObjects(text);
        if (objects.Count > 0)
        {
            // Return the last one as it's most likely the final response
            return objects[objects.Count - 1];
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        return "{}";
    }

    public static List<string> ExtractAllJsonObjects(string text)
    {
        var results = new List<string>();
        if (string.IsNullOrEmpty(text)) return results;

        int braceCount = 0;
        int startPos = -1;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '{')
                {
                    if (braceCount == 0) startPos = i;
                    braceCount++;
                }
                else if (c == '}')
                {
                    if (braceCount > 0)
                    {
                        braceCount--;
                        if (braceCount == 0 && startPos != -1)
                        {
                            results.Add(text[startPos..(i + 1)]);
                            startPos = -1;
                        }
                    }
                }
            }
        }

        return results;
    }
}
