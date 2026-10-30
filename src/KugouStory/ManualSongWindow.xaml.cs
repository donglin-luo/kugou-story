using System.Windows;
using KugouStory.Core;

namespace KugouStory;
public partial class ManualSongWindow : Window
{
    public Song? Song { get; private set; }
    public ManualSongWindow() => InitializeComponent();
    private void Confirm(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleInput.Text) || string.IsNullOrWhiteSpace(ArtistInput.Text))
        { Message.Text = "请填写歌名和歌手，避免同名歌曲混淆。"; return; }
        Song = new Song(TitleInput.Text.Trim(), ArtistInput.Text.Trim(), Source: "手动输入");
        DialogResult = true;
    }
}
