using System.Text.RegularExpressions;

namespace KugouStory.Core;

public sealed record Song(string Title, string Artist, string Album = "", string Source = "", bool IsPlaying = true)
{
    public string Key => $"{Title.Trim().ToUpperInvariant()}\u001f{Artist.Trim().ToUpperInvariant()}\u001f{Album.Trim().ToUpperInvariant()}";
    public string ArtistLabel => string.IsNullOrWhiteSpace(Artist) ? "歌手信息未提供" : Artist;
}

public static partial class SongTitleParser
{
    // Only called for windows belonging to KuGou processes, never other players.
    public static Song? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var text = title.Trim();
        if (text is "酷狗音乐" or "酷狗" || text.Contains("桌面歌词") || text.Contains("传歌")) return null;
        text = BrandSuffix().Replace(text, "").Trim();
        text = StatePrefix().Replace(text, "").Trim();
        // KuGou's legacy window caption is "歌手 - 歌名 - 酷狗音乐".
        var pieces = text.Split(" - ", 2, StringSplitOptions.TrimEntries);
        if (pieces.Length != 2) pieces = text.Split(" – ", 2, StringSplitOptions.TrimEntries);
        if (pieces.Length != 2 || pieces.Any(string.IsNullOrWhiteSpace)) return null;
        if (pieces.Any(p => p is "酷狗音乐" or "酷狗")) return null;
        return new Song(pieces[1], pieces[0], Source: "酷狗窗口");
    }

    [GeneratedRegex(@"\s*[-–—|_]\s*酷狗(?:音乐)?(?:播放器)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BrandSuffix();
    [GeneratedRegex(@"^(?:\[?(?:正在播放|播放中|已暂停|暂停)\]?[:：\s-]*)")]
    private static partial Regex StatePrefix();
}

// Reject one-off transitional captions. Playback status can change without regenerating a story.
public sealed class SongStabilizer
{
    private string? _candidate;
    private int _count;
    public Song? Push(Song? song)
    {
        if (song is null) { _candidate = null; _count = 0; return null; }
        if (_candidate == song.Key) _count++; else { _candidate = song.Key; _count = 1; }
        return _count >= 2 ? song : null;
    }
}
