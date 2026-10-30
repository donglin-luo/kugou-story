using System.IO;
using System.Windows;

namespace KugouStory;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, "Local\\KugouStory.SingleInstance", out var created);
        if (!created)
        {
            System.Windows.MessageBox.Show("听见已在运行，可在系统托盘中双击图标打开。", "听见");
            Shutdown(); return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show("操作未完成，请重试。\n" + args.Exception.GetType().Name, "听见");
            args.Handled = true;
        };
        MainWindow = new MainWindow(e.Args.Contains("--preview"));
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _mutex?.Dispose(); base.OnExit(e); }
}
