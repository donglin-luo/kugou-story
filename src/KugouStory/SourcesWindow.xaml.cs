using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using KugouStory.Core;

namespace KugouStory;
public partial class SourcesWindow : Window
{
    public SourcesWindow(StoryResult story)
    {
        InitializeComponent(); SourceList.ItemsSource = story.Sources.Select(source => source with { Site = SourceRules.Publisher(source.Url) }).ToList();
        Summary.Text = $"后台已阅读网页资料，并由 AI 对照原文核对介绍。\n核对时间：{story.CheckedAt.ToLocalTime():yyyy-MM-dd HH:mm}。点击下方链接可查看完整上下文。";
    }
    private void OpenSource(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string url } || !SourceRules.IsPublicWebUrl(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { Summary.Text = "无法打开浏览器，请稍后重试。"; }
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
