using System.Text.Json;
using AiChatApp.Endpoints;

namespace AiChatApp.Services;

/// <summary>
/// 全ユーザー共通のニュースキャッシュ。毎朝7時の自動収集と手動更新（force=true）の両方をサポートする。
/// </summary>
public class NewsCacheService
{
    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private List<NewsItem> _cachedNews = new();
    private DateTime _lastUpdated = DateTime.MinValue;

    public NewsCacheService()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(dataDir);
        _cacheFilePath = Path.Combine(dataDir, "news_cache.json");
        LoadFromDisk();
    }

    public DateTime LastUpdated => _lastUpdated;

    /// <summary>
    /// キャッシュ済みニュースを取得する。force=true またはキャッシュが空の場合はAIで再収集する。
    /// </summary>
    public async Task<List<NewsItem>> GetNewsAsync(AiService ai, bool force = false)
    {
        if (!force && _cachedNews.Count > 0)
        {
            return _cachedNews;
        }

        return await RefreshAsync(ai);
    }

    public async Task<List<NewsItem>> RefreshAsync(AiService ai)
    {
        await _refreshLock.WaitAsync();
        try
        {
            var localTime = DateTime.Now;
            var newsPrompt = $@"
你是一个专业的国际新闻编辑，可以联网搜索最新资讯。当前时间为: {localTime:yyyy-MM-dd HH:mm}。
请搜索并整理最近24小时内美国、中国、日本三国的真实重大新闻，覆盖科技、财经、社会、政治等领域。

要求:
1. 返回5条新闻，三个国家都要覆盖到（每个国家至少1条）。
2. 必须是真实发生的新闻事件，禁止虚构或编造。
3. 每条新闻尽量给出真实有效的原文链接。

返回格式必须是标准的 JSON 数组，包含且仅包含这 5 条新闻。每个新闻对象包含以下字段:
- title: 新闻标题 (简短有力，限制在 20 个字以内)
- category: 分类 (例如: 科技, 人工智能, 财经, 商业, 政治, 社会)
- country: 国家 (美国/中国/日本/国际)
- summary: 新闻摘要 (2句话左右，控制在 50 个字以内)
- time: 时间提示 (例如: 10分钟前, 1小时前, 今天)
- url: 新闻原文链接 (真实有效的URL，若确实无法获取请留空字符串)

请务必只返回合法的 JSON 数组，不要包含任何 markdown 块标记（如 ```json 等）或多余的解释文字。
";
            try
            {
                var newsJson = await ai.ExecuteCliDirectAsync(newsPrompt, ai.DefaultProvider, "你是 AI 新闻编辑，具备联网搜索能力，只输出真实新闻的 JSON。", outputFormat: "json");
                var cleanJson = newsJson.Trim();
                if (cleanJson.StartsWith("```"))
                {
                    var lines = cleanJson.Split('\n');
                    var filteredLines = lines.Where(l => !l.Trim().StartsWith("```")).ToArray();
                    cleanJson = string.Join("\n", filteredLines);
                }

                var parsed = JsonSerializer.Deserialize<List<NewsItem>>(cleanJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null && parsed.Count > 0)
                {
                    _cachedNews = parsed;
                    _lastUpdated = DateTime.UtcNow;
                    SaveToDisk();
                }
            }
            catch (Exception)
            {
                if (_cachedNews.Count == 0)
                {
                    _cachedNews = GetFallbackNews();
                    _lastUpdated = DateTime.UtcNow;
                }
            }

            return _cachedNews;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return;
            var json = File.ReadAllText(_cacheFilePath);
            var data = JsonSerializer.Deserialize<NewsCacheData>(json);
            if (data?.Items != null)
            {
                _cachedNews = data.Items;
                _lastUpdated = data.UpdatedAt;
            }
        }
        catch (Exception)
        {
            // キャッシュ読み込み失敗は無視し、空の状態から再収集させる
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var data = new NewsCacheData { Items = _cachedNews, UpdatedAt = _lastUpdated };
            File.WriteAllText(_cacheFilePath, JsonSerializer.Serialize(data));
        }
        catch (Exception)
        {
            // 保存失敗は致命的ではないため無視
        }
    }

    private static List<NewsItem> GetFallbackNews() => new()
    {
        new NewsItem("GPT-5 预计今秋发布", "人工智能", "据业内人士透露，新一代模型 GPT-5 预计在秋季发布，将在多模态和复杂推理上取得重大突破。", "10分钟前") { Country = "美国" },
        new NewsItem("量子计算取得突破", "前沿科技", "科研人员在超导量子比特纠缠度上取得进展，为实用化量子计算迈出坚实一步。", "1小时前") { Country = "中国" },
        new NewsItem("清洁能源投资创纪录", "财经", "最新报告显示，今年全球在太阳能和风能等清洁能源领域的投资额再创历史新高。", "2小时前") { Country = "日本" },
    };

    private class NewsCacheData
    {
        public List<NewsItem> Items { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }
}
