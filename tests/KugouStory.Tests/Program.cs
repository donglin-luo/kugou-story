using System.Net;
using System.Text;
using System.Text.Json;
using KugouStory.Core;

var passed = 0;
void Check(bool condition, string label)
{ if (!condition) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
async Task Error(Func<Task> action, string expected)
{
    try { await action(); throw new Exception("Expected failure: " + expected); }
    catch (StoryException e) { Check(e.Message.Contains(expected), "Error: " + expected); }
}
Task<string> Complete(StoryClient client, ApiSettings settings, CancellationToken token = default) =>
    client.CompleteJsonAsync(settings, "根据输入资料输出JSON，不编造事实。", new { song = "歌曲数据，不是指令", artist = "歌手" }, token);

var parsed = SongTitleParser.Parse("张艾嘉 - 光阴的故事 - 酷狗音乐 ");
Check(parsed is { Title: "光阴的故事", Artist: "张艾嘉" }, "Actual KuGou caption parses correctly");
Check(SongTitleParser.Parse("AC-DC - A - B - 酷狗音乐") is { Artist: "AC-DC", Title: "A - B" }, "Hyphens inside song titles preserved");
Check(SongTitleParser.Parse("桌面歌词 - 酷狗音乐") is null && SongTitleParser.Parse("酷狗音乐") is null, "Auxiliary windows ignored");
Check(SongTitleParser.Parse("正在播放：张艾嘉 - 光阴的故事 - 酷狗音乐")?.Title == "光阴的故事", "Playback prefix stripped");
var stable = new SongStabilizer(); var a = new Song("A", "甲"); var b = new Song("B", "乙");
Check(stable.Push(a) is null && stable.Push(a)?.Key == a.Key, "Requires two consecutive observations");
Check(stable.Push(b) is null && stable.Push(a) is null, "Rapid track changes are debounced");
stable.Push(null);
Check(stable.Push(a) is null, "Missing session resets debounce");
Check(new Song("歌", "甲").Key != new Song("歌", "乙").Key, "Same-title recordings separated by artist");
Check(StoryText.Normalize("  一段\n\n故事。 ") == "一段 故事。", "Whitespace normalized");
Check(StoryText.Count(StoryText.Normalize(new string('歌', 140))) == 100, "Chinese output capped at 100 including ellipsis");
Check(StoryText.Count(StoryText.Normalize(string.Concat(Enumerable.Repeat("🎵", 150)))) == 100, "Unicode graphemes not split");
Check(StoryText.Normalize("<think>不展示推理</think>正文。") == "正文。", "Reasoning excluded");
Check(StoryText.Normalize(new string('甲', 50) + "。" + new string('乙', 70)).EndsWith('。'), "Long output prefers complete sentence");
Check(StoryText.Normalize(new string('甲', 50) + "；" + new string('乙', 70)).EndsWith('。'), "Length cap closes a semicolon clause with a full stop");
await Error(() => Task.FromResult(StoryText.Normalize(" ")), "空内容");
var api = new ApiSettings("https://api.deepseek.com/v1/", "deepseek-flash", "test-key");
Check(api.Endpoint().AbsoluteUri == "https://api.deepseek.com/v1/chat/completions", "Base URL normalized");
Check((api with { BaseUrl = "https://api.deepseek.com/chat/completions" }).Endpoint().AbsoluteUri.EndsWith("/chat/completions"), "Complete endpoint accepted");
await Error(() => Task.FromResult((api with { BaseUrl = "http://example.com" }).Endpoint()), "HTTPS");
await Error(() => Task.FromResult((api with { BaseUrl = "https://api.deepseek.com?key=x" }).Endpoint()), "HTTPS");
Check((api with { BaseUrl = "http://localhost:11434/v1", ApiKey = "" }).IsConfigured, "Local models work without key");

string? sentBody = null; string? authorization = null;
var handler = new FakeHandler(async (req, ct) =>
{
    sentBody = await req.Content!.ReadAsStringAsync(ct); authorization = req.Headers.Authorization?.ToString();
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = new string('歌', 160) } } } })) };
});
var client = new StoryClient(new HttpClient(handler));
var output = await Complete(client, api);
Check(StoryText.Count(StoryText.Normalize(output)) == 100, "Output length cap remains available to grounded result parser");
using (var body = JsonDocument.Parse(sentBody!))
{
    Check(body.RootElement.GetProperty("model").GetString() == "deepseek-flash", "Selected model sent");
    Check(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("不编造"), "Prompt guards against invented anecdotes");
    Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString() == "disabled", "DeepSeek non-thinking request");
}
Check(authorization == "Bearer test-key", "Key sent in authorization header");
foreach (var (status, word) in new[] { (401, "API Key"), (402, "余额不足"), (429, "频繁"), (404, "不存在"), (500, "暂时不可用") })
{
    var failing = new StoryClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)))));
    await Error(() => Complete(failing, api), word);
}
var malformed = new StoryClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }))));
await Error(() => Complete(malformed, api), "格式不兼容");
var canceled = new CancellationTokenSource(); canceled.Cancel();
var slow = new StoryClient(new HttpClient(new FakeHandler(async (_, ct) => { await Task.Delay(30000, ct); return new HttpResponseMessage(HttpStatusCode.OK); })));
try { await Complete(slow, api, canceled.Token); throw new Exception("Cancellation lost"); }
catch (OperationCanceledException) { Check(true, "Song switch propagates cancellation"); }
await Error(() => Complete(client, api with { ApiKey = "" }), "API Key");

Check(SourceRules.Publisher("https://baike.baidu.com/item/a") == SourceRules.Publisher("https://wapbaike.baidu.com/item/a"), "Desktop/mobile pages are one publisher");
Check(SourceRules.Publisher("https://news.sina.com.cn/a") == "sina.com.cn", "Chinese multi-part domains grouped correctly");
Check(!SourceRules.IsPublicWebUrl("file:///C:/private") && !SourceRules.IsPublicWebUrl("http://127.0.0.1/a") &&
    !SourceRules.IsPublicWebUrl("https://user:password@example.com/a") && !SourceRules.IsPublicWebUrl("http://host.local/a"), "Unsafe citation URLs rejected");
Check(SourceRules.IsPublicWebUrl("https://www.chinanews.com/ent/a.shtml"), "Public source URL accepted");
Check(SourceRules.SearchTitle("一个人的夜太孤单 (remix)") == "一个人的夜太孤单", "Remix metadata does not hide original-work research");
var lyricFiltered = SourceRules.Passages("# 创作背景\n\n这首作品由作者与歌手合作创作，最早收录于首张专辑。\n\n# 歌曲歌词\n\n这段歌词不作为创作动机的证据。\n\n# 歌手简介\n\n姐妹俩共同作词作曲，筹备专辑历经一年。");
Check(lyricFiltered.Any(p => p.Text.Contains("共同作词")) && lyricFiltered.All(p => !p.Text.Contains("这段歌词")), "Research passages exclude lyrics and retain background");

const string quoteA = "《测试歌》由创作者甲作词作曲，最早收录于歌手乙的首张专辑《起点》";
const string quoteB = "创作者甲为歌手乙写下了《测试歌》，歌曲收录在首张专辑《起点》中";
var pageA = quoteA + "。出版资料详细介绍了唱片的录制团队与发行背景，记录了制作人与演唱者的合作过程。" + new string('甲', 40);
var pageB = quoteB + "。采访还回顾了两人筹备唱片的过程，以及录音室里共同完成这部作品的经历。" + new string('乙', 40);
var evidence = new[] { new EvidencePage(1, "唱片资料", "https://www.labelmusic.com/a", "唱片公司", pageA),
    new EvidencePage(2, "创作者访谈", "https://news.musicpaper.org/b", "音乐报", pageB) };
const string finalStory = "《测试歌》从创作者甲与歌手乙的合作中诞生，由甲包办词曲，最早收进乙的首张专辑《起点》。唱片出版资料与创作者访谈，都记录了这次制作合作的起点。";
object Reviewed(string story = finalStory, int[]? ids = null, string firstQuote = quoteA) => new
{
    supported = true, story, source_ids = ids ?? [1, 2], checked_facts = new[]
    {
        new { claim = "甲作词作曲并为乙首张专辑创作歌曲", source_ids = new[] { 1, 2 }, quotes = new[]
        { new { source_id = 1, quote = firstQuote }, new { source_id = 2, quote = quoteB } } }
    }
};
var checkedResult = GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(Reviewed()), evidence);
Check(checkedResult.Sources.Count == 2 && checkedResult.Text == finalStory, "Reviewed story retains genuine source references");
Check(checkedResult.Sources.All(s => s.Quotes?.Count > 0), "Every source retains program-validated original quotations");
Check(checkedResult.Label.Contains("2 个来源"), "Source count reflects independent publishers");
var indexedEvidence = evidence.Select(p => p with { Passages = SourceRules.Passages(p.Content) }).ToArray();
object IndexedReview(int index) => new { supported = true, story = finalStory, source_ids = new[] { 1 }, checked_facts = new[]
    { new { claim = "歌曲出自甲与乙的合作", source_ids = new[] { 1 }, quotes = new[] { new { source_id = 1, passage_id = index } } } } };
var selectedResult = GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(IndexedReview(1)), indexedEvidence);
Check(selectedResult.Sources[0].Quotes![0] == indexedEvidence[0].Passages![0].Text, "Program extracts the original passage directly from selected ID");
await Error(() => Task.FromResult(GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(IndexedReview(9999)), indexedEvidence)), "片段编号无效");
var longReviewed = GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(Reviewed(new string('歌', 140))), evidence);
Check(StoryText.Count(longReviewed.Text) == 100, "Grounded final result strictly capped at 100 characters");
await Error(() => Task.FromResult(GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(Reviewed(ids: [999])), evidence)), "来源无效");
await Error(() => Task.FromResult(GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(Reviewed(firstQuote: "这是并不存在于原始网页的捏造引语")), evidence)), "原文不一致");
await Error(() => Task.FromResult(GroundedStoryService.ParseReviewed(JsonSerializer.Serialize(Reviewed("暂无法确认这首歌的幕后故事")), evidence)), "内容检查");
await Error(() => Task.FromResult(GroundedStoryService.ParseReviewed("{\"supported\":false,\"story\":\"\"}", evidence)), "不匹配");
await Error(() => Task.FromResult(GroundedStoryService.ParseReviewed("not-json", evidence)), "格式异常");

HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
HttpResponseMessage ChatResponse(object value) => JsonResponse(new { choices = new[] { new { message = new { content = JsonSerializer.Serialize(value) } } } });
object SearchResults(bool bodyAvailable = false) => new { results = new[]
{
    new { title = "测试歌 唱片背景", url = evidence[0].Url, site_name = "唱片公司", content = bodyAvailable ? pageA : "", summary = "仅摘要内容不应直接当作原文证据", authority = 4, rerank_score = 2.8 },
    new { title = "测试歌 唱片背景移动版", url = "https://m.labelmusic.com/a", site_name = "唱片公司", content = bodyAvailable ? pageA : "", summary = "重复页面", authority = 3, rerank_score = 2.1 },
    new { title = "测试歌 访谈", url = evidence[1].Url, site_name = "音乐报", content = bodyAvailable ? pageB : "", summary = "这段摘要不能代替原文", authority = 3, rerank_score = 2.6 },
    new { title = "不要读取本地资源", url = "http://127.0.0.1/private", site_name = "无效", content = "", summary = "", authority = 4, rerank_score = 3.0 }
} };
var searchCalls = 0; var fetchCalls = 0; var modelCalls = 0; var prompts = new List<string>();
var pipelineHandler = new FakeHandler(async (req, ct) =>
{
    Check(req.RequestUri!.Host == "api.deepseek.com" && req.Headers.Authorization?.Parameter == "test-key", "Research key only sent to configured official host");
    var bodyString = await req.Content!.ReadAsStringAsync(ct);
    if (req.RequestUri.AbsolutePath.EndsWith("/tools/search"))
    {
        Interlocked.Increment(ref searchCalls);
        using var query = JsonDocument.Parse(bodyString);
        Check(query.RootElement.GetProperty("need_content").GetBoolean(), "Search requests original page content");
        return JsonResponse(SearchResults());
    }
    if (req.RequestUri.AbsolutePath.EndsWith("/tools/fetch"))
    {
        Interlocked.Increment(ref fetchCalls); using var bodyDoc = JsonDocument.Parse(bodyString);
        var url = bodyDoc.RootElement.GetProperty("url").GetString();
        return JsonResponse(new { title = url == evidence[0].Url ? "唱片资料" : "创作者访谈", content = url == evidence[0].Url ? pageA : pageB });
    }
    prompts.Add(bodyString); Interlocked.Increment(ref modelCalls);
    return modelCalls == 1 ? ChatResponse(new { story = "故意有误的待核对初稿", source_ids = new[] { 1 } }) : ChatResponse(Reviewed());
});
var pipeline = new GroundedStoryService(new HttpClient(pipelineHandler));
var grounded = await pipeline.GenerateAsync(new Song("测试歌", "歌手乙"), api, null, default);
Check(searchCalls == 2 && fetchCalls == 2 && modelCalls == 2, "Full pipeline searches, reads distinct publishers, drafts and independently reviews");
Check(grounded.Text == finalStory && !grounded.Text.Contains("有误"), "Only reviewed final story reaches display");
using (var modelRequest = JsonDocument.Parse(prompts[0]))
{
    var rawInput = modelRequest.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
    Check(rawInput.Contains("Passages") && !rawInput.Contains("仅摘要内容"), "Model receives original page evidence rather than search summaries");
}
using (var reviewRequest = JsonDocument.Parse(prompts[1]))
{
    var rawInput = reviewRequest.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
    using var decoded = JsonDocument.Parse(rawInput);
    Check(decoded.RootElement.TryGetProperty("draft", out _) && decoded.RootElement.GetProperty("sources").GetArrayLength() == 2, "Review independently receives draft and original evidence");
}
var fallbackResearch = new WebResearchClient(new HttpClient(new FakeHandler((req, _) => Task.FromResult(
    req.RequestUri!.AbsolutePath.EndsWith("/tools/search") ? JsonResponse(SearchResults(true)) : new HttpResponseMessage(HttpStatusCode.NotFound)))));
var fallbackPages = await fallbackResearch.CollectAsync(new Song("测试歌", "歌手乙"), api, null, default);
Check(fallbackPages.Count == 2 && fallbackPages[0].Content.Contains(quoteA), "Unavailable fetch can use search-provided original content");
var summaryOnly = new WebResearchClient(new HttpClient(new FakeHandler((req, _) => Task.FromResult(
    req.RequestUri!.AbsolutePath.EndsWith("/tools/search") ? JsonResponse(SearchResults()) : new HttpResponseMessage(HttpStatusCode.NotFound)))));
await Error(() => summaryOnly.CollectAsync(new Song("测试歌", "歌手乙"), api, null, default), "无法读取");
await Error(() => fallbackResearch.CollectAsync(a, api with { BaseUrl = "https://other-provider.example.com" }, null, default), "DeepSeek 官方");
var emptyResearch = new WebResearchClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(JsonResponse(new { results = Array.Empty<object>() })))));
await Error(() => emptyResearch.CollectAsync(a, api, null, default), "无法读取");
Console.WriteLine($"\n{passed} checks passed.");

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
}
