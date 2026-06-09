using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AiChatApp.Services.Infrastructure;

/// <summary>
/// Compresses AI context (system prompt + conversation history) using Headroom
/// before sending to CLI providers, reducing token usage by 60-95%.
/// Falls back gracefully if headroom-ai is not installed.
/// </summary>
public class HeadroomCompressionService
{
    private readonly ILogger<HeadroomCompressionService> _logger;
    private readonly IConfiguration _config;
    private string? _scriptPath;
    private bool? _headroomAvailable;

    public HeadroomCompressionService(ILogger<HeadroomCompressionService> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public bool IsEnabled => _config.GetValue("AiSettings:HeadroomCompression:Enabled", true);
    public int MinCharsToCompress => _config.GetValue("AiSettings:HeadroomCompression:MinChars", 800);

    private string ScriptPath => _scriptPath ??= Path.Combine(AppContext.BaseDirectory, "Scripts", "headroom_compress.py");

    /// <summary>
    /// Compresses systemPrompt + conversationHistory into a single compacted string.
    /// Returns null if compression is disabled, unavailable, or the input is too short.
    /// </summary>
    public async Task<CompressionResult?> CompressAsync(string? systemPrompt, string conversationHistory, string model = "gemini")
    {
        if (!IsEnabled) return null;

        var combined = (systemPrompt ?? "") + conversationHistory;
        if (combined.Length < MinCharsToCompress) return null;

        // Build OpenAI-style messages array
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new { role = "system", content = systemPrompt });
        if (!string.IsNullOrWhiteSpace(conversationHistory))
            messages.Add(new { role = "user", content = conversationHistory });

        var payload = new { messages, model };
        var payloadJson = JsonSerializer.Serialize(payload);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                ArgumentList = { ScriptPath },
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            await process.StandardInput.WriteAsync(payloadJson);
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;

            if (string.IsNullOrWhiteSpace(output)) return null;

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                _logger.LogDebug("Headroom error: {Error}", err.GetString());
                return null;
            }

            if (root.TryGetProperty("warning", out var warn))
            {
                if (_headroomAvailable != false)
                {
                    _logger.LogInformation("Headroom: {Warning}", warn.GetString());
                    _headroomAvailable = false;
                }
                return null;
            }

            _headroomAvailable = true;

            if (!root.TryGetProperty("messages", out var compressedMsgs))
                return null;

            var originalChars = root.TryGetProperty("original_chars", out var oc) ? oc.GetInt32() : 0;
            var compressedChars = root.TryGetProperty("compressed_chars", out var cc) ? cc.GetInt32() : 0;

            // Reassemble compressed messages back into prompt parts
            string? compressedSystem = null;
            string? compressedHistory = null;

            foreach (var msg in compressedMsgs.EnumerateArray())
            {
                var role = msg.TryGetProperty("role", out var r) ? r.GetString() : null;
                var content = msg.TryGetProperty("content", out var c) ? c.GetString() : null;

                if (role == "system") compressedSystem = content;
                else if (role == "user") compressedHistory = content;
            }

            if (originalChars > 0 && compressedChars > 0)
            {
                var ratio = (1.0 - (double)compressedChars / originalChars) * 100;
                _logger.LogInformation("Headroom compression: {Original} → {Compressed} chars ({Ratio:F1}% reduction)",
                    originalChars, compressedChars, ratio);
            }

            return new CompressionResult(compressedSystem, compressedHistory, originalChars, compressedChars);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Headroom compression failed, using original prompt");
            return null;
        }
    }
}

public record CompressionResult(
    string? CompressedSystemPrompt,
    string? CompressedHistory,
    int OriginalChars,
    int CompressedChars
)
{
    public double ReductionRatio => OriginalChars > 0 ? (1.0 - (double)CompressedChars / OriginalChars) : 0;
}
