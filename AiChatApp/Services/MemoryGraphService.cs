using AiChatApp.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AiChatApp.Services;

/// <summary>
/// 記憶間の関係性（Knowledge Graph / Mindmap）を管理・探索するサービス。
/// </summary>
public class MemoryGraphService
{
    private readonly MemoryFileService _fileService;
    private readonly ILogger<MemoryGraphService> _logger;

    // グラフ構造: Entity -> List of related Memory IDs/FileNames
    private readonly ConcurrentDictionary<string, HashSet<string>> _adjList = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastBuildTime = DateTime.MinValue;

    public MemoryGraphService(MemoryFileService fileService, ILogger<MemoryGraphService> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    /// <summary>
    /// 全ての記憶ファイルから関係性をスキャンし、メモリ内グラフを構築する。
    /// 最後に構築してから1分以内であればキャッシュを再利用する。
    /// </summary>
    public void BuildGraph(int userId, bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastBuildTime < TimeSpan.FromMinutes(1)) return;

        var memories = _fileService.GetMemoriesForUser(userId);
        _adjList.Clear();

        foreach (var mem in memories)
        {
            if (string.IsNullOrEmpty(mem.Relations) || string.IsNullOrEmpty(mem.SourceFile)) continue;

            var entities = mem.Relations.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(IsValidEntity);

            foreach (var entity in entities)
            {
                _adjList.AddOrUpdate(entity, 
                    _ => new HashSet<string> { mem.SourceFile },
                    (_, set) => { set.Add(mem.SourceFile); return set; });
                
                var tags = mem.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(IsValidEntity);
                foreach (var tag in tags)
                {
                     _adjList.AddOrUpdate(tag, 
                        _ => new HashSet<string> { mem.SourceFile },
                        (_, set) => { set.Add(mem.SourceFile); return set; });
                }
            }
        }
        _lastBuildTime = DateTime.UtcNow;
        _logger.LogInformation("[MemoryGraph] Graph built with {Count} entities.", _adjList.Count);
    }

    /// <summary>
    /// 指定された記憶に関連する他の記憶を探索する（1ステップ）。
    /// </summary>
    public List<LongTermMemory> GetRelatedMemories(LongTermMemory seed, int userId, int limit = 3)
    {
        if (string.IsNullOrEmpty(seed.Relations) && string.IsNullOrEmpty(seed.Tags)) return [];

        var relatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = (seed.Relations ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Union(seed.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(e => e.Trim());

        foreach (var entity in entities)
        {
            if (_adjList.TryGetValue(entity, out var files))
            {
                foreach (var f in files)
                {
                    if (f != seed.SourceFile) relatedFiles.Add(f);
                }
            }
        }

        var allMemories = _fileService.GetMemoriesForUser(userId);
        return allMemories
            .Where(m => m.SourceFile != null && relatedFiles.Contains(m.SourceFile))
            .Take(limit)
            .ToList();
    }

/// <summary>
/// 前端表示用のMermaidグラフ形式を生成する。
/// 支持分级加载和展开（Hierarchical Drill-Down）。
/// </summary>
public string GenerateMermaidGraph(int userId, string? rootId = null, int depth = 1, string? collapsedIds = null)
{
    var memories = _fileService.GetMemoriesForUser(userId);
    if (!memories.Any())
    {
        return "graph TD\n  no_mem[\"No memories found yet. Start chatting to build your knowledge map!\"]";
    }

    var collapsedSet = new HashSet<string>((collapsedIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
    bool isExpanded = !string.IsNullOrEmpty(rootId);

    IEnumerable<LongTermMemory> visibleMemories;

    if (!string.IsNullOrEmpty(rootId))
    {
        var rootMem = memories.FirstOrDefault(m => GetNodeId(m) == rootId);
        if (rootMem != null)
        {
            visibleMemories = GetNeighbors(rootMem, memories, depth);
        }
        else
        {
            var relName = rootId.StartsWith("rel_") ? rootId.Substring(4).Replace("_", " ") : rootId;
            visibleMemories = memories.Where(m => 
                (m.Relations ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(r => r.Trim().Equals(relName, StringComparison.OrdinalIgnoreCase)) ||
                (m.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(t => t.Trim().Equals(relName, StringComparison.OrdinalIgnoreCase))
            ).Take(10).ToList();
        }
    }
    else
    {
        visibleMemories = memories.OrderByDescending(m => m.CreatedAt).Take(10).ToList();
    }

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("graph LR");

    var definedNodes = new HashSet<string>();
    var memoriesToProcess = visibleMemories.ToList();

    // 0. Overview Root
    if (!isExpanded)
    {
        sb.AppendLine("  overview_root[\"🧠 My Knowledge Map\"]:::centerNode");
        definedNodes.Add("overview_root");

        // Grouping by Tag to create hierarchy
        var tagGroups = memoriesToProcess
            .Select(m => new { Mem = m, Tag = (m.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "General" })
            .GroupBy(x => x.Tag);

        foreach (var group in tagGroups)
        {
            var tagId = "tag_" + System.Text.RegularExpressions.Regex.Replace(group.Key, @"[^a-zA-Z0-9]", "_");
            
            // Skip children if tag node is collapsed
            bool isTagCollapsed = collapsedSet.Contains(tagId);

            if (!definedNodes.Contains(tagId))
            {
                var style = isTagCollapsed ? ":::collapsedNode" : ":::tagNode";
                sb.AppendLine($"  {tagId}(\"{group.Key}\"){style}");
                sb.AppendLine($"  overview_root --> {tagId}");
                definedNodes.Add(tagId);
            }

            if (!isTagCollapsed)
            {
                foreach (var item in group)
                {
                    var mem = item.Mem;
                    var nodeId = GetNodeId(mem);
                    var nodeName = (mem.Tags ?? "").Split(',').Skip(1).FirstOrDefault()?.Trim() 
                                   ?? mem.Content.Split('\n')[0].Trim();
                    if (nodeName.Length > 25) nodeName = nodeName[..22] + "...";

                    if (!definedNodes.Contains(nodeId))
                    {
                        var nodeStyle = collapsedSet.Contains(nodeId) ? ":::collapsedNode" : "";
                        sb.AppendLine($"  {nodeId}[{SanitizeLabel(nodeName)}]{nodeStyle}");
                        definedNodes.Add(nodeId);
                    }
                    sb.AppendLine($"  {tagId} --> {nodeId}");
                }
            }
        }
    }
    else
    {
        // 1. Output Nodes (Expanded Mode)
        foreach (var mem in memoriesToProcess)
        {
            if (string.IsNullOrEmpty(mem.SourceFile)) continue;
            
            var tagsArr = (mem.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
            var nodeName = tagsArr.Length > 0 ? tagsArr[0].Trim() : "Memory";
            var nodeId = GetNodeId(mem);

            if (!definedNodes.Contains(nodeId))
            {
                var isCollapsed = collapsedSet.Contains(nodeId);
                var style = nodeId == rootId ? ":::centerNode" : (isCollapsed ? ":::collapsedNode" : "");
                sb.AppendLine($"  {nodeId}[{SanitizeLabel(nodeName)}]{style}");
                definedNodes.Add(nodeId);
            }
        }
    }

    // 2. Output Edges (and virtual nodes)
    if (isExpanded)
    {
        var edges = new HashSet<string>();

        foreach (var mem in memoriesToProcess)
        {
            if (string.IsNullOrEmpty(mem.SourceFile) || string.IsNullOrEmpty(mem.Relations)) continue;

            var nodeId = GetNodeId(mem);
            
            // If parent is collapsed, don't show children/edges
            if (collapsedSet.Contains(nodeId)) continue;

            var relations = mem.Relations.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .Where(IsValidEntity);

            foreach (var rel in relations)
            {
                if (string.IsNullOrEmpty(rel)) continue;

                var relId = "rel_" + System.Text.RegularExpressions.Regex.Replace(rel, @"[^a-zA-Z0-9]", "_");

                var targetMem = memories.FirstOrDefault(m => 
                    (m.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Any(t => t.Equals(rel, StringComparison.OrdinalIgnoreCase)));

                if (targetMem != null && targetMem.SourceFile != null)
                {
                    var targetId = GetNodeId(targetMem);
                    
                    if (!definedNodes.Contains(targetId))
                    {
                        var targetName = (targetMem.Tags ?? "").Split(',').FirstOrDefault()?.Trim() ?? "Memory";
                        var targetStyle = collapsedSet.Contains(targetId) ? ":::collapsedNode" : "";
                        sb.AppendLine($"  {targetId}[{SanitizeLabel(targetName)}]{targetStyle}");
                        definedNodes.Add(targetId);
                    }

                    var edge = $"{nodeId} --> {targetId}";
                    if (!edges.Contains(edge))
                    {
                        sb.AppendLine($"  {edge}");
                        edges.Add(edge);
                    }
                }
                else
                {
                    if (!definedNodes.Contains(relId))
                    {
                        sb.AppendLine($"  {relId}(({SanitizeLabel(rel)})):::virtualNode");
                        definedNodes.Add(relId);
                    }
                    
                    var edge = $"{nodeId} -.-> {relId}";
                    if (!edges.Contains(edge))
                    {
                        sb.AppendLine($"  {edge}");
                        edges.Add(edge);
                    }
                }
            }
        }
    }

    // 3. Styles (at the end)
    sb.AppendLine("  classDef centerNode fill:#f96,stroke:#333,stroke-width:4px,font-size:18px,font-weight:bold;");
    sb.AppendLine("  classDef tagNode fill:#6366f1,color:#fff,stroke:#4f46e5,stroke-width:2px,font-size:16px,font-weight:bold;");
    sb.AppendLine("  classDef virtualNode fill:#eee,stroke:#999,stroke-dasharray:5,5,font-size:14px;");
    sb.AppendLine("  classDef collapsedNode fill:#fff,stroke:#6366f1,stroke-width:2px,stroke-dasharray: 5,5,font-size:14px;");

    return sb.ToString();
}

private static string SanitizeLabel(string text)
{
    if (string.IsNullOrEmpty(text)) return "\"memory\"";

    var sanitized = text
        .Replace("\\", "")
        .Replace("\"", "'")
        .Replace("\r", " ")
        .Replace("\n", " ");

    // Remove characters that are known to break Mermaid labels
    sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[<>\[\]{}|`()#*]", "");

    if (sanitized.Length > 40)
        sanitized = sanitized[..37] + "...";

    return $"\"{sanitized.Trim()}\"";
}

private static bool IsValidEntity(string entity)
{
    if (string.IsNullOrWhiteSpace(entity)) return false;
    if (entity.Length < 2) return false;
    if (System.Text.RegularExpressions.Regex.IsMatch(entity, @"^\d+$")) return false;
    if (System.Text.RegularExpressions.Regex.IsMatch(entity, @"^(relevanceScore|accessCount|userId|createdAt|lastAccessedAt)\s*:\s*\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
    return true;
}

    private string GetNodeId(LongTermMemory mem)
    {
        var rawName = Path.GetFileNameWithoutExtension(mem.SourceFile ?? "unknown");
        var safeId = System.Text.RegularExpressions.Regex.Replace(rawName, @"[^a-zA-Z0-9]+", "_").Trim('_');
        return "node_" + safeId;
    }

    private HashSet<LongTermMemory> GetNeighbors(LongTermMemory root, List<LongTermMemory> allMemories, int depth)
    {
        var result = new HashSet<LongTermMemory> { root };
        if (depth <= 0) return result;

        var currentLevel = new List<LongTermMemory> { root };
        for (int i = 0; i < depth; i++)
        {
            var nextLevel = new List<LongTermMemory>();
            foreach (var mem in currentLevel)
            {
                // 正向关系
                var relations = (mem.Relations ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim());
                foreach (var rel in relations)
                {
                    var target = allMemories.FirstOrDefault(m => 
                        (m.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim())
                        .Any(t => t.Equals(rel, StringComparison.OrdinalIgnoreCase)));
                    if (target != null && !result.Contains(target))
                    {
                        result.Add(target);
                        nextLevel.Add(target);
                    }
                }

                // 反向关系
                var memTags = (mem.Tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim());
                foreach (var tag in memTags)
                {
                    var sources = allMemories.Where(m => 
                        (m.Relations ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .Any(r => r.Equals(tag, StringComparison.OrdinalIgnoreCase)));
                    foreach (var src in sources)
                    {
                        if (!result.Contains(src))
                        {
                            result.Add(src);
                            nextLevel.Add(src);
                        }
                    }
                }
            }
            currentLevel = nextLevel;
            if (result.Count > 50) break; // 防止爆炸
        }
        return result;
    }
}
