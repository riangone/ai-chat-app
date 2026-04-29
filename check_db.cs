using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using AiChatApp.Data;
using AiChatApp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=AiChatApp/AiChatApp.db"));
var serviceProvider = services.BuildServiceProvider();

using var scope = serviceProvider.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

var latestSteps = db.AgentSteps
    .OrderByDescending(s => s.Id)
    .Take(20)
    .ToList();

Console.WriteLine($"{"Id",-5} | {"Model",-20} | {"Tokens (P/C/T)",-20} | {"Output (first 50 chars)",-30}");
Console.WriteLine(new string('-', 85));

foreach (var s in latestSteps)
{
    string tokens = $"{s.PromptTokens}/{s.CompletionTokens}/{s.TotalTokens}";
    string output = s.Output.Length > 50 ? s.Output.Substring(0, 47) + "..." : s.Output.Replace("\n", " ");
    Console.WriteLine($"{s.Id,-5} | {s.Model,-20} | {tokens,-20} | {output}");
}
