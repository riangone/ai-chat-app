using System.Diagnostics;

namespace AiChatApp.Services;

public class AiService
{
    public async Task<string> GetResponseAsync(string prompt, string provider = "gemini")
    {
        string fileName = "gemini";
        string arguments = $"\"{prompt.Replace("\"", "\\\"")}\"";

        if (provider == "gh-copilot")
        {
            fileName = "copilot";
            arguments = $"-p \"{prompt.Replace("\"", "\\\"")}\" --yolo";
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return $"Error: Could not start {provider} CLI.";
            
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                return $"Error from {provider}: {error}";
            }
            
            return string.IsNullOrWhiteSpace(output) ? "No response received." : output;
        }
        catch (Exception ex)
        {
            return $"Exception: {ex.Message}";
        }
    }
}
