using System.Globalization;
using System.Text.RegularExpressions;

namespace KugouStory.Core;

public static partial class StoryText
{
    public const int Limit = 100;
    public static int Count(string text) => new StringInfo(text).LengthInTextElements;

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new StoryException("AI 返回了空内容，请重试。");
        var text = Reasoning().Replace(raw, "");
        text = Whitespace().Replace(text, " ").Trim().Trim('"', '“', '”', '`');
        if (string.IsNullOrWhiteSpace(text)) throw new StoryException("AI 未返回介绍正文，请重试。");
        var indexes = StringInfo.ParseCombiningCharacters(text);
        if (indexes.Length <= Limit) return text;
        var prefix = text[..indexes[Limit - 1]];
        // Prefer ending a complete sentence; if there is none, show an explicit ellipsis.
        var end = prefix.LastIndexOfAny(['。', '！', '？', '；', '，']);
        return end >= 35 ? prefix[..(end + 1)].TrimEnd('；', '，') + (prefix[end] is '；' or '，' ? "。" : "") : prefix.TrimEnd('，', '、', ',', ' ') + "…";
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Reasoning();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

public sealed class StoryException(string message) : Exception(message);

public sealed record ApiSettings(string BaseUrl, string Model, string ApiKey)
{
    public static ApiSettings Default => new("https://api.deepseek.com", "deepseek-flash", "");
    public bool IsLocal => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Model) && (IsLocal || !string.IsNullOrWhiteSpace(ApiKey));

    public Uri Endpoint()
    {
        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new StoryException("接口地址需使用 HTTPS；本机服务可用 http://localhost。请勿填写查询参数。");
        var root = uri.AbsoluteUri.TrimEnd('/');
        return new Uri(root.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? root : root + "/chat/completions");
    }
}
