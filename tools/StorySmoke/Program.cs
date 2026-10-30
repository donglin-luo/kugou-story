using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using KugouStory.Services;
using KugouStory.Core;

Console.OutputEncoding = Encoding.UTF8;
var store = new LocalStore();
var api = store.Api();
if (!api.IsConfigured || api.Endpoint().Host != "api.deepseek.com") { Console.WriteLine("Official DeepSeek key not configured."); return; }
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(65) };
string Option(string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
var song = new Song(Option("--song", "光阴的故事"), Option("--artist", "张艾嘉"));
var progress = new Progress<string>(Console.WriteLine);
var timer = System.Diagnostics.Stopwatch.StartNew();
try
{
    Directory.CreateDirectory("test-results");
    void Diagnostic(string stage, string data) { if (args.Contains("--diagnose")) File.WriteAllText("test-results/" + stage + ".json", data); }
    var result = await new GroundedStoryService(http, Diagnostic).GenerateAsync(song, api, progress, default);
    var output = JsonSerializer.Serialize(new { characters = StoryText.Count(result.Text), seconds = timer.Elapsed.TotalSeconds, result },
        new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    Console.WriteLine(output);
    Directory.CreateDirectory("test-results");
    File.WriteAllText("test-results/live-grounded.json", output);
    if (args.Contains("--save")) store.PutStory(song, api, result);
}
catch (Exception e) { Console.WriteLine("FAILED: " + e.Message); Environment.ExitCode = 1; }
