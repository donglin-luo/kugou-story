using Windows.Media.Control;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

Console.OutputEncoding = Encoding.UTF8;
try
{
    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    foreach (var session in manager.GetSessions())
    {
        var p = await session.TryGetMediaPropertiesAsync();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { source = session.SourceAppUserModelId, title = p.Title, artist = p.Artist, album = p.AlbumTitle, status = session.GetPlaybackInfo().PlaybackStatus.ToString() }));
    }
}
catch (Exception e) { Console.WriteLine("SMTC: " + e.Message); }
var pids = Process.GetProcessesByName("KuGou").Select(p => p.Id).ToHashSet();
Native.EnumWindows((h, _) =>
{
    Native.GetWindowThreadProcessId(h, out var pid);
    if (!pids.Contains((int)pid)) return true;
    var title = new StringBuilder(2048); Native.GetWindowText(h, title, title.Capacity);
    var cls = new StringBuilder(256); Native.GetClassName(h, cls, cls.Capacity);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { handle = h.ToInt64(), pid, title = title.ToString(), cls = cls.ToString() }));
    return true;
}, 0);
static class Native
{
    public delegate bool Callback(nint hwnd, nint data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb, nint data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(nint h, StringBuilder b, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(nint h, StringBuilder b, int max);
}
