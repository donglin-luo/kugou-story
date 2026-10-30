using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KugouStory.Core;

public sealed record SourceReference(int Id, string Title, string Url, string Site, List<string>? Quotes = null)
{
    public string QuotePreview => Quotes is { Count: > 0 } ? "核对摘录：" + string.Join("；", Quotes.Select(q => "“" + q + "”")) : "";
}
public sealed record StoryResult(string Text, List<SourceReference> Sources, DateTimeOffset CheckedAt)
{
    public string Label => $"已对照资料 · {Sources.Select(s => SourceRules.Publisher(s.Url)).Distinct().Count()} 个来源";
}
public sealed record EvidencePassage(int Id, string Text);
public sealed record EvidencePage(int Id, string Title, string Url, string Site, string Content, List<EvidencePassage>? Passages = null);
internal sealed record SearchHit(string Title, string Url, string Site, string Content, string Summary, double Score, int Authority);

public static partial class SourceRules
{
    public static bool IsPublicWebUrl(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo) && !uri.IsLoopback &&
        uri.Host.Contains('.') && !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !IPAddress.TryParse(uri.Host, out _);

    public static string Publisher(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var parts = uri.IdnHost.ToLowerInvariant().Split('.');
        var count = parts.Length >= 3 && (parts[^2] is "com" or "org" or "net" or "co") && parts[^1].Length == 2 ? 3 : 2;
        return string.Join('.', parts.TakeLast(Math.Min(count, parts.Length)));
    }

    public static string CanonicalUrl(string url)
    { var uri = new Uri(url); return uri.GetLeftPart(UriPartial.Path).TrimEnd('/'); }

    public static string Limit(string text, int length) => text.Length <= length ? text : text[..length];

    public static string Extract(string text, string songTitle)
    {
        var clean = Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", "");
        var lines = clean.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 1).ToArray();
        if (clean.Length <= 8500) return clean;
        var selected = new SortedSet<int>(Enumerable.Range(0, Math.Min(4, lines.Length)));
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains(songTitle, StringComparison.OrdinalIgnoreCase) && !Background().IsMatch(lines[i])) continue;
            for (var j = Math.Max(0, i - 1); j <= Math.Min(lines.Length - 1, i + 2); j++) selected.Add(j);
        }
        return Limit(string.Join('\n', selected.Select(i => lines[i])), 8500);
    }

    public static List<EvidencePassage> Passages(string text)
    {
        // Index real source spans once; the model selects IDs rather than retyping quotations.
        var prose = Regex.Replace(text,
            @"(?ms)^\s*(?:\#{1,6}\s*)?(?:歌曲歌词|歌曲歌詞|歌词|歌詞|Lyrics)\s*\r?$.*?(?=^\s*(?:\#{1,6}\s*)?(?:翻唱版本|歌手简介|歌手簡介|发行信息|發行信息|参考资料|參考資料|创作背景|創作背景|获奖记录)\s*\r?$|\z)", "");
        var all = Regex.Split(prose, @"(?<=[。！？；])|\r?\n\s*\r?\n")
            .Select((s, i) => new { Text = s.Trim(), Index = i }).Where(s => s.Text.Length >= 16).ToList();
        var relevant = all.Where(s => Background().IsMatch(s.Text)).ToList();
        var spans = (relevant.Count > 0 ? relevant : all)
            .OrderByDescending(s => Background().Matches(s.Text).Count + (s.Text.Length > 50 ? 2 : 0))
            .Take(26).OrderBy(s => s.Index).Select(s => s.Text).ToList();
        var result = new List<EvidencePassage>();
        foreach (var span in spans)
        {
            for (var offset = 0; offset < span.Length; offset += 230)
            {
                var part = span.Substring(offset, Math.Min(230, span.Length - offset));
                if (part.Length >= 8 && result.All(p => p.Text != part)) result.Add(new EvidencePassage(result.Count + 1, part));
            }
            if (result.Count >= 100) break;
        }
        return result.Take(100).ToList();
    }

    public static string SearchTitle(string title) => Regex.Replace(title,
        @"\s*[（(][^()（）]*(?:remix|live|dj|现场|現場|版)[^()（）]*[)）]\s*$", "", RegexOptions.IgnoreCase).Trim();

    [GeneratedRegex("创作|創作|制作|製作|录制|錄製|发行|發行|专辑|專輯|访谈|訪談|采访|採訪|作词|作詞|作曲|首唱|背景|灵感|靈感|姐妹|release|record|wrote|inspir|producer|composer|lyricist|debut|album|written|collaborat", RegexOptions.IgnoreCase)]
    private static partial Regex Background();
}

public sealed class WebResearchClient(HttpClient http)
{
    private static Uri ToolEndpoint(ApiSettings api, string tool)
    {
        var endpoint = api.Endpoint();
        if (endpoint.Scheme != "https" || !endpoint.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(api.ApiKey))
            throw new StoryException("自动检索需要 DeepSeek 官方 API。请在设置中使用 https://api.deepseek.com 和有效的 DeepSeek API Key。");
        return new Uri($"https://api.deepseek.com/v1/tools/{tool}");
    }

    public async Task<List<EvidencePage>> CollectAsync(Song song, ApiSettings api, IProgress<string>? progress, CancellationToken token)
    {
        progress?.Report("正在搜索歌曲创作与录制背景…");
        var title = SourceRules.Limit(SourceRules.SearchTitle(song.Title).Replace('"', ' '), 65);
        var artist = SourceRules.Limit(song.Artist.Replace('"', ' '), 30);
        var queries = new[] { $"{title} {artist} 创作背景 采访", $"{title} 作词 作曲 首唱 专辑 发行 背景" };
        var searches = await Task.WhenAll(queries.Select(q => SearchAsync(q, api, token)));
        var candidates = SelectHits(searches.SelectMany(s => s), song);
        if (candidates.Select(h => SourceRules.Publisher(h.Url)).Distinct().Count() < 2)
        {
            var extra = await SearchAsync($"{title} {artist} 唱片 故事", api, token);
            candidates = SelectHits(candidates.Concat(extra), song);
        }
        progress?.Report("正在读取原文，筛选同名歌曲与重复转载…");
        var pages = await Task.WhenAll(candidates.Take(5).Select(h => ReadPageAsync(h, song, api, token)));
        var valid = pages.Where(p => p is not null).Cast<EvidencePage>()
            .GroupBy(p => SourceRules.Publisher(p.Url)).Select(g => g.First()).Take(4).ToList();
        if (valid.Count == 0) throw new StoryException("网页资料暂时无法读取。请检查网络后点“重新检索”，会重新搜索并核对。");
        return valid.Select((p, i) => p with { Id = i + 1, Passages = SourceRules.Passages(p.Content) }).ToList();
    }

    private static List<SearchHit> SelectHits(IEnumerable<SearchHit> hits, Song song) => hits
        .Where(h => SourceRules.IsPublicWebUrl(h.Url) && !string.IsNullOrWhiteSpace(h.Title))
        .GroupBy(h => SourceRules.CanonicalUrl(h.Url)).Select(g => g.OrderByDescending(h => h.Score).First())
        .OrderByDescending(h => (h.Title.Contains(song.Title, StringComparison.OrdinalIgnoreCase) ? 2 : 0) +
            (h.Summary.Contains(song.Artist, StringComparison.OrdinalIgnoreCase) && song.Artist.Length > 0 ? 1 : 0) +
            Math.Max(0, h.Authority) * 0.35 + h.Score)
        .GroupBy(h => SourceRules.Publisher(h.Url)).Select(g => g.First()).Take(5).ToList();

    private async Task<List<SearchHit>> SearchAsync(string query, ApiSettings api, CancellationToken token)
    {
        using var json = await ProviderRequest.PostAsync(http, ToolEndpoint(api, "search"), api.ApiKey,
            new { query = SourceRules.Limit(query, 128), count = 8, need_content = true }, "联网检索", 30, token);
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new StoryException("检索接口返回格式异常，请稍后重试。");
        return results.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object).Select(r => new SearchHit(
            Text(r, "title"), Text(r, "url"), Text(r, "site_name"), Text(r, "content"), Text(r, "summary"),
            r.TryGetProperty("rerank_score", out var score) && score.TryGetDouble(out var s) ? s : 0,
            r.TryGetProperty("authority", out var auth) && auth.TryGetInt32(out var a) ? a : 0)).ToList();
    }

    private async Task<EvidencePage?> ReadPageAsync(SearchHit hit, Song song, ApiSettings api, CancellationToken token)
    {
        var content = hit.Content;
        var title = hit.Title;
        try
        {
            using var json = await ProviderRequest.PostAsync(http, ToolEndpoint(api, "fetch"), api.ApiKey, new { url = hit.Url }, "网页读取", 22, token);
            var fetched = Text(json.RootElement, "content");
            if (fetched.Length >= 80) { content = fetched; var name = Text(json.RootElement, "title"); if (name.Length > 0) title = name; }
        }
        catch (StoryException) when (content.Length >= 80) { /* need_content returns page text, never use a generated summary as evidence. */ }
        catch (StoryException) { return null; }
        if (content.Length < 80) return null;
        return new EvidencePage(0, title, hit.Url, string.IsNullOrWhiteSpace(hit.Site) ? SourceRules.Publisher(hit.Url) : hit.Site,
            SourceRules.Extract(content, song.Title));
    }

    internal static string Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}
