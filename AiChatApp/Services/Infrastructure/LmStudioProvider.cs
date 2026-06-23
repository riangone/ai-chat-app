using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AiChatApp.Services.Infrastructure;

public class LmStudioProvider
{
    private readonly HttpClient _http;
    private readonly ILogger<LmStudioProvider> _logger;
    private readonly string _baseUrl;
    private readonly string _defaultModel;

    public LmStudioProvider(HttpClient http, IConfiguration config, ILogger<LmStudioProvider> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = config["LmStudio:BaseUrl"] ?? "http://localhost:1234/v1";
        _defaultModel = config["LmStudio:DefaultModel"] ?? "";
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/models");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> GetModelsAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_baseUrl}/models");
            if (!response.IsSuccessStatusCode) return new List<string>();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var m in data.EnumerateArray())
                {
                    if (m.TryGetProperty("id", out var id))
                        models.Add(id.GetString() ?? "");
                }
            }
            return models.Where(m => !string.IsNullOrEmpty(m)).ToList()!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch LM Studio models");
            return new List<string>();
        }
    }

    public async Task<string> GetResponseAsync(string prompt, string systemPrompt = "", string model = "")
    {
        var payload = BuildPayload(prompt, systemPrompt, model, stream: false);
        var response = await _http.PostAsync($"{_baseUrl}/chat/completions", payload);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                return content.GetString() ?? "";
        }
        return "";
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(string prompt, string systemPrompt = "", string model = "")
    {
        var payload = BuildPayload(prompt, systemPrompt, model, stream: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = payload
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("data: "))
            {
                var data = line[6..];
                if (data == "[DONE]") yield break;
                string? text = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content))
                        {
                            text = content.GetString();
                        }
                    }
                }
                catch (JsonException) { }
                if (!string.IsNullOrEmpty(text)) yield return text;
            }
        }
    }

    private StringContent BuildPayload(string prompt, string systemPrompt, string model, bool stream)
    {
        var messages = new List<object>();
        if (!string.IsNullOrEmpty(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        messages.Add(new { role = "user", content = prompt });

        var modelName = string.IsNullOrEmpty(model) ? _defaultModel : model;
        var body = new
        {
            messages,
            stream,
            model = string.IsNullOrEmpty(modelName) ? null : modelName
        };
        return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    }
}
