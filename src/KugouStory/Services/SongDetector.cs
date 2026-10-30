using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KugouStory.Core;
using Windows.Media.Control;

namespace KugouStory.Services;

public sealed class SongDetector
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private DateTime _retryAfter;

    public async Task<Song?> ReadAsync(CancellationToken token)
    {
        try
        {
            if (_manager is null && DateTime.UtcNow >= _retryAfter)
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(3), token);
            if (_manager is not null)
            {
                var sessions = _manager.GetSessions()
                    .Where(s => IsKugou(s.SourceAppUserModelId))
                    .OrderByDescending(s => s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                foreach (var session in sessions)
                {
                    var status = session.GetPlaybackInfo().PlaybackStatus;
                    if (status is not (GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing or
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)) continue;
                    var p = await session.TryGetMediaPropertiesAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(2), token);
                    if (string.IsNullOrWhiteSpace(p.Title)) continue;
                    return new Song(p.Title.Trim(), p.Artist?.Trim() ?? "", p.AlbumTitle?.Trim() ?? "", "Windows 媒体信息",
                        status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException)
        { _manager = null; _retryAfter = DateTime.UtcNow.AddSeconds(30); }
        return ReadWindowCaption();
    }

    public static bool IsKugou(string source) => source.Contains("kugou", StringComparison.OrdinalIgnoreCase) || source.Contains("酷狗");

    public static Song? ReadWindowCaption()
    {
        var processes = Process.GetProcessesByName("KuGou");
        try
        {
            var ids = processes.Select(p => (uint)p.Id).ToHashSet();
            if (ids.Count == 0) return null;
            Song? found = null;
            Native.EnumWindows((handle, _) =>
            {
                Native.GetWindowThreadProcessId(handle, out var pid);
                if (!ids.Contains(pid)) return true;
                var title = new StringBuilder(2048);
                Native.GetWindowText(handle, title, title.Capacity);
                var text = title.ToString();
                // Don't mistake auxiliary KuGou windows for a song.
                if (!text.Contains("酷狗")) return true;
                found = SongTitleParser.Parse(text);
                return found is null;
            }, 0);
            return found;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static class Native
    {
        internal delegate bool Callback(nint handle, nint data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(Callback callback, nint data);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint handle, StringBuilder text, int max);
    }
}
