using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KugouStory.Core;

namespace KugouStory.Services;

public sealed class Preferences
{
    public string BaseUrl { get; set; } = ApiSettings.Default.BaseUrl;
    public string Model { get; set; } = ApiSettings.Default.Model;
    public string EncryptedKey { get; set; } = "";
    public bool Topmost { get; set; } = true;
    public double Opacity { get; set; } = 0.97;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 450;
}

public sealed record CacheEntry(string Key, StoryResult? Story, DateTime SavedAt);

public sealed class LocalStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KugouStory");
    private readonly string _settingsFile = Path.Combine(Folder, "settings.json");
    private readonly string _cacheFile = Path.Combine(Folder, "stories.json");
    private readonly List<CacheEntry> _cache;
    public Preferences Preferences { get; private set; }
    public string? StorageNotice { get; private set; }

    public LocalStore()
    {
        Preferences = Read<Preferences>(_settingsFile) ?? new Preferences();
        _cache = Read<List<CacheEntry>>(_cacheFile) ?? [];
    }

    private T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { StorageNotice = "本地配置读取失败，已使用默认设置。"; return default; }
    }

    public ApiSettings Api()
    {
        var key = "";
        if (!string.IsNullOrEmpty(Preferences.EncryptedKey))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(Preferences.EncryptedKey), null, DataProtectionScope.CurrentUser);
                key = Encoding.UTF8.GetString(bytes);
                CryptographicOperations.ZeroMemory(bytes);
            }
            catch (Exception e) when (e is CryptographicException or FormatException)
            { StorageNotice = "密钥无法解密，请在设置中重新填写。"; }
        }
        return new ApiSettings(Preferences.BaseUrl, Preferences.Model, key);
    }

    public void SaveApi(ApiSettings settings)
    {
        var encrypted = string.IsNullOrWhiteSpace(settings.ApiKey) ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.ApiKey.Trim()), null, DataProtectionScope.CurrentUser));
        var oldUrl = Preferences.BaseUrl; var oldModel = Preferences.Model; var oldKey = Preferences.EncryptedKey;
        Preferences.BaseUrl = settings.BaseUrl.Trim(); Preferences.Model = settings.Model.Trim(); Preferences.EncryptedKey = encrypted;
        try { SavePreferences(); }
        catch { Preferences.BaseUrl = oldUrl; Preferences.Model = oldModel; Preferences.EncryptedKey = oldKey; throw; }
    }

    public void SavePreferences() => Write(_settingsFile, Preferences);

    // Key includes provider/model. Switching models should not silently reuse another model's result.
    private static string CacheKey(Song song, ApiSettings api) => $"v2-grounded-passages|{api.BaseUrl.TrimEnd('/')}|{api.Model}|{song.Key}";
    public StoryResult? GetStory(Song song, ApiSettings api)
    {
        var key = CacheKey(song, api);
        var entry = _cache.LastOrDefault(e => e.Key == key && e.SavedAt > DateTime.UtcNow.AddDays(-30));
        return entry?.Story is { Sources.Count: > 0 } story && story.Sources.All(s => SourceRules.IsPublicWebUrl(s.Url))
            ? story with { Text = StoryText.Normalize(story.Text) } : null;
    }

    public void PutStory(Song song, ApiSettings api, StoryResult story)
    {
        var key = CacheKey(song, api);
        _cache.RemoveAll(e => e.Story is null || e.Key == key || e.SavedAt < DateTime.UtcNow.AddDays(-30));
        _cache.Add(new CacheEntry(key, story with { Text = StoryText.Normalize(story.Text) }, DateTime.UtcNow));
        if (_cache.Count > 200) _cache.RemoveRange(0, _cache.Count - 200);
        try { Write(_cacheFile, _cache); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { StorageNotice = "介绍已生成，但本地缓存保存失败。"; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Folder);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
