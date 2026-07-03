using AiChatApp.Models;

namespace AiChatApp.Services.Pulse;

public interface IPulseSource
{
    string SourceType { get; }
    Task<IReadOnlyList<PulseItem>> DiscoverAsync(Project project, CancellationToken ct);
}

public record PulseItem(string SourceKey, string Description, string? BaseRef);
