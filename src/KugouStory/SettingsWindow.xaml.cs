using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using KugouStory.Core;
using KugouStory.Services;

namespace KugouStory;

public partial class SettingsWindow : Window
{
    private readonly LocalStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false });
    public SettingsWindow(LocalStore store)
    {
        InitializeComponent(); _store = store;
        var api = store.Api(); KeyInput.Password = api.ApiKey;
        BaseUrlInput.Text = api.BaseUrl; ModelInput.Text = api.Model;
        OpacityInput.Value = store.Preferences.Opacity;
        Message.Text = store.StorageNotice ?? "填写后保存，当前歌曲会自动生成介绍。";
    }
    private ApiSettings ReadApi()
    {
        var api = new ApiSettings(BaseUrlInput.Text.Trim(), ModelInput.Text.Trim(), KeyInput.Password.Trim());
        _ = api.Endpoint();
        if (!api.IsConfigured) throw new StoryException("请填写 API Key 和模型名称。");
        return api;
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            var api = ReadApi(); _store.SaveApi(api);
            _store.Preferences.Opacity = OpacityInput.Value; _store.SavePreferences();
            DialogResult = true;
        }
        catch (Exception ex) when (ex is StoryException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { Message.Text = ex is StoryException ? ex.Message : "配置无法保存，请检查本地目录权限。"; }
    }
    private async void TestConnection(object sender, RoutedEventArgs e)
    {
        try
        {
            var api = ReadApi(); TestButton.IsEnabled = false; Message.Text = "正在测试检索、网页读取和故事核对…";
            var progress = new Progress<string>(text => { if (!_lifetime.IsCancellationRequested) Message.Text = text; });
            await new GroundedStoryService(_http).GenerateAsync(new Song("光阴的故事", "张艾嘉"), api, progress, _lifetime.Token);
            if (!_lifetime.IsCancellationRequested) Message.Text = "检索与核对成功。保存后即可自动显示歌曲故事。";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { if (!_lifetime.IsCancellationRequested) Message.Text = ex is StoryException ? ex.Message : "测试失败，请检查设置。"; }
        finally { if (!_lifetime.IsCancellationRequested) TestButton.IsEnabled = true; }
    }
    private void OpenProvider(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://platform.deepseek.com") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { Message.Text = "无法打开浏览器，请访问 platform.deepseek.com。"; }
    }
    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Window_Closed(object? sender, EventArgs e) { _lifetime.Cancel(); _http.Dispose(); }
}
