using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KugouStory.Core;
using KugouStory.Services;
using Forms = System.Windows.Forms;

namespace KugouStory;

public partial class MainWindow : Window
{
    private readonly LocalStore _store = new();
    private readonly SongDetector _detector = new();
    private readonly SongStabilizer _stabilizer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private CancellationTokenSource? _generation;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _pinMenu;
    private readonly bool _preview;
    private Song? _current;
    private StoryResult? _displayedStory;
    private bool _manual;
    private bool _busy;
    private bool _needsGeneration;
    private int _missingPolls;
    private int _revision;

    public MainWindow(bool preview)
    {
        InitializeComponent();
        _preview = preview;
        var p = _store.Preferences;
        // Upgrade saved dimensions too, so existing installations gain enough room for 100 characters.
        Width = Math.Clamp(p.Width, 420, 600); Height = Math.Clamp(p.Height, 450, 650);
        Opacity = Math.Clamp(p.Opacity, 0.65, 1); Topmost = p.Topmost;
        // Restore only on an attached screen; this also handles monitor changes.
        if (p.Left is double left && p.Top is double top && double.IsFinite(left) && double.IsFinite(top) &&
            left + Width > SystemParameters.VirtualScreenLeft && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
            top + Height > SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        { Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
          Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height); }
        else { Left = SystemParameters.WorkArea.Right - Width - 28; Top = SystemParameters.WorkArea.Bottom - Height - 28; }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示小窗", null, (_, _) => Dispatcher.Invoke(Restore));
        _pinMenu = new Forms.ToolStripMenuItem("取消置顶");
        _pinMenu.Click += (_, _) => Dispatcher.Invoke(ToggleTopmost);
        menu.Items.Add(_pinMenu);
        menu.Items.Add("AI 设置", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add("手动输入歌曲", null, (_, _) => Dispatcher.Invoke(ManualSong));
        menu.Items.Add("恢复自动识别", null, (_, _) => Dispatcher.Invoke(ResumeAutomatic));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        _tray = new Forms.NotifyIcon { Text = "听见 · 酷狗歌曲故事", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
        UpdatePin();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_preview)
        {
            _current = new Song("不得不爱", "潘玮柏、弦子", Source: "演示");
            RenderSong(_current); PlaybackLabel.Text = "界面演示 · 非实时识别";
            SetStory("陈子鸿为潘玮柏新专辑寻找女声合唱，觉得唱《醉清风》的弦子嗓音干净、契合风格，便向潘玮柏推荐了她。录制时两人分开录音，直到拍MV才第一次见面，这首对唱在隔空合作中完成。而这些细节，也是这首歌背后的故事。", "100 字排版演示 · 未调用 API");
            FooterStatus.Text = "演示模式"; return;
        }
        if (!_store.Api().IsConfigured)
        { FooterStatus.Text = "首次使用：点击 ⚙ 填写 API Key"; }
        _ = PollAsync();
        await Task.CompletedTask;
    }

    private async Task PollAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                if (!_manual)
                {
                    var detected = await _detector.ReadAsync(_lifetime.Token);
                    if (_manual) continue;
                    var song = _stabilizer.Push(detected);
                    if (detected is null) _missingPolls++; else _missingPolls = 0;
                    // Immediately cancel an obsolete request as soon as another track is observed.
                    if (detected is not null && _current is not null && detected.Key != _current.Key)
                    {
                        CancelGeneration(); _needsGeneration = true;
                        _displayedStory = null; SourcesButton.IsEnabled = false;
                        StoryBody.Text = "正在确认新歌曲…"; CharCount.Text = "切歌中";
                    }
                    if (song is not null)
                    {
                        var changed = _current?.Key != song.Key;
                        _current = song; RenderSong(song);
                        if (changed || _needsGeneration) { _needsGeneration = false; _ = GenerateAsync(false); }
                    }
                    else if (_missingPolls >= 4 && _current is not null)
                    {
                        CancelGeneration(); _current = null;
                        _displayedStory = null; SourcesButton.IsEnabled = false;
                        SongTitle.Text = "等一首歌的故事"; SongArtist.Text = "请在酷狗中播放歌曲";
                        PlaybackLabel.Text = "尚未识别到歌曲"; StatusDot.Fill = System.Windows.Media.Brushes.SlateGray;
                        StoryBody.Text = "酷狗开始播放后，这里会自动更新。若此版本无法识别，可在托盘菜单中手动输入歌曲。";
                        CharCount.Text = "100 字以内"; StoryMeta.Text = "等待歌曲";
                        FooterStatus.Text = "自动识别中";
                    }
                }
                await Task.Delay(1200, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                FooterStatus.Text = "歌曲识别暂不可用，正在重连";
                try { await Task.Delay(3000, _lifetime.Token); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private void RenderSong(Song song)
    {
        SongTitle.Text = song.Title; SongArtist.Text = song.ArtistLabel;
        PlaybackLabel.Text = _manual ? "手动歌曲 · 托盘菜单可恢复自动" : song.Source == "酷狗窗口" ? "已识别 · 播放状态未提供" : song.IsPlaying ? "酷狗正在播放" : "酷狗已暂停";
        StatusDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(song.IsPlaying ? "#6DE5CD" : "#D4AC73"));
        _tray.Text = ("听见 · " + song.Title)[..Math.Min(63, ("听见 · " + song.Title).Length)];
    }

    private void CancelGeneration()
    {
        _revision++;
        _generation?.Cancel(); _generation?.Dispose(); _generation = null;
        _busy = false; RefreshButton.IsEnabled = true;
    }

    private async Task GenerateAsync(bool force)
    {
        CancelGeneration();
        if (_current is not { } song) return;
        if (_preview) return;
        var api = _store.Api();
        if (!force && _store.GetStory(song, api) is { } cached)
        { ShowResult(cached, true); FooterStatus.Text = song.Source; return; }
        if (!api.IsConfigured)
        {
            StoryBody.Text = "歌曲已识别。点击右上角 ⚙ 填入 DeepSeek API Key，即可自动检索背景资料、核对并生成故事。";
            CharCount.Text = "等待连接"; StoryMeta.Text = "密钥仅保存在本机"; FooterStatus.Text = "需要配置 DeepSeek";
            RefreshButton.Content = "连接"; return;
        }
        _generation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _generation.Token; var revision = _revision;
        _busy = true; RefreshButton.IsEnabled = false;
        var previous = _displayedStory;
        if (previous is null) { StoryBody.Text = "正在为这首歌搜索创作资料…"; CharCount.Text = "检索中"; }
        StoryMeta.Text = "搜索网页 → 阅读原文 → 整理与核对"; FooterStatus.Text = "正在联网检索";
        var succeeded = false;
        try
        {
            var progress = new Progress<string>(message =>
            {
                if (revision != _revision || token.IsCancellationRequested) return;
                FooterStatus.Text = message;
                if (previous is null) StoryBody.Text = message;
            });
            var result = await new GroundedStoryService(_http).GenerateAsync(song, api, progress, token);
            if (token.IsCancellationRequested || revision != _revision || _current?.Key != song.Key) return;
            _store.PutStory(song, api, result);
            ShowResult(result, false); FooterStatus.Text = song.Source; succeeded = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (revision != _revision || token.IsCancellationRequested) return;
            if (previous is null)
            { StoryBody.Text = e is StoryException ? e.Message : "资料整理失败，请点击“重新检索”。"; CharCount.Text = "待重试"; StoryMeta.Text = "歌曲识别会继续工作"; }
            else { ShowResult(previous, true); StoryMeta.Text = "重新检索未完成 · 保留上次核对的介绍"; }
            FooterStatus.Text = "点击重新检索，或检查网络与设置";
        }
        finally
        {
            if (revision == _revision) { _busy = false; RefreshButton.IsEnabled = true; RefreshButton.Content = succeeded ? "换一篇" : "重新检索"; }
        }
    }

    private void SetStory(string text, string meta)
    { StoryBody.Text = StoryText.Normalize(text); CharCount.Text = $"{StoryText.Count(StoryBody.Text)} / 100 字"; StoryMeta.Text = meta; RefreshButton.Content = "换一篇"; }
    private void ShowResult(StoryResult result, bool cached)
    { _displayedStory = result; SetStory(result.Text, result.Label + (cached ? " · 缓存" : "")); SourcesButton.IsEnabled = result.Sources.Count > 0; }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is not System.Windows.Controls.Button && e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void TogglePin(object sender, RoutedEventArgs e) => ToggleTopmost();
    private void ToggleTopmost()
    {
        Topmost = !Topmost; UpdatePin(); _store.Preferences.Topmost = Topmost;
        try { _store.SavePreferences(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { FooterStatus.Text = "置顶已切换，本地偏好保存失败"; }
    }
    private void UpdatePin()
    {
        var label = Topmost ? "取消置顶" : "置顶";
        PinButton.Content = label; PinButton.ToolTip = Topmost ? "取消窗口置顶" : "让小窗保持在其他窗口上方";
        System.Windows.Automation.AutomationProperties.SetName(PinButton, label);
        PinButton.Foreground = (System.Windows.Media.Brush)FindResource(Topmost ? "Accent" : "Muted");
        _pinMenu.Text = label; _pinMenu.Checked = Topmost;
    }
    private void HideToTray(object sender, RoutedEventArgs e) => Hide();
    private void ExitApp(object sender, RoutedEventArgs e) => Close();
    private void OpenSettings(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowSettings()
    {
        var dialog = new SettingsWindow(_store) { Owner = this };
        if (dialog.ShowDialog() == true) { Opacity = _store.Preferences.Opacity; _ = GenerateAsync(false); }
    }
    private async void RefreshStory(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_store.Api().IsConfigured) { ShowSettings(); return; }
        if (_current is null) { FooterStatus.Text = "请先在酷狗中播放歌曲"; return; }
        await GenerateAsync(true);
    }
    private void SearchSources(object sender, RoutedEventArgs e)
    {
        if (_displayedStory is not { } story) return;
        new SourcesWindow(story) { Owner = this }.ShowDialog();
    }
    private void ManualSong()
    {
        Restore(); var dialog = new ManualSongWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Song is { } song)
        { _manual = true; _current = song; _displayedStory = null; SourcesButton.IsEnabled = false; RenderSong(song); _ = GenerateAsync(false); }
    }
    private void ResumeAutomatic()
    {
        CancelGeneration(); _manual = false; _current = null;
        _displayedStory = null; SourcesButton.IsEnabled = false;
        PlaybackLabel.Text = "正在恢复自动识别"; FooterStatus.Text = "等待酷狗媒体信息";
    }
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _lifetime.Cancel(); CancelGeneration(); _tray.Visible = false; _tray.Dispose(); _http.Dispose();
        var p = _store.Preferences; p.Left = Left; p.Top = Top; p.Width = Width; p.Height = Height; p.Topmost = Topmost;
        try { _store.SavePreferences(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _lifetime.Dispose();
    }
}
