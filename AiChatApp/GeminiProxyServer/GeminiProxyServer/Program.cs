using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add CORS to allow requests from the Chrome Extension
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.WebHost.UseUrls("http://localhost:5001");

var app = builder.Build();

app.UseCors("AllowAll");

app.MapPost("/api/parse", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data" });
    }

    var form = await request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file provided." });
    }

    string prompt = form["prompt"].ToString();
    if (string.IsNullOrEmpty(prompt))
    {
        prompt = "Parse this document and return a structured JSON response corresponding to the legacy system schema.";
    }

    // Force json output instruction if not present
    if (!prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase))
    {
        prompt += "\nOutput ONLY valid JSON without any markdown formatting or explanations.";
    }

    var tempFilePath = Path.GetTempFileName() + Path.GetExtension(file.FileName);
    using (var stream = new FileStream(tempFilePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gemini",
            Arguments = $"--prompt \"{prompt.Replace("\"", "\\\"")}\" \"{tempFilePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return Results.StatusCode(500);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            return Results.Problem($"Gemini CLI failed: {error}");
        }

        try 
        {
            var cleanOutput = output.Trim();
            if (cleanOutput.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                cleanOutput = cleanOutput.Substring(7);
                if (cleanOutput.EndsWith("```"))
                {
                    cleanOutput = cleanOutput.Substring(0, cleanOutput.Length - 3);
                }
                cleanOutput = cleanOutput.Trim();
            }

            var jsonElement = JsonSerializer.Deserialize<JsonElement>(cleanOutput);
            return Results.Ok(jsonElement);
        }
        catch
        {
            return Results.Ok(new { text = output });
        }
    }
    finally
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", version = "1.0.0" }));

app.Run();
