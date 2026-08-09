using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Threading;
using RocoPilot.Installer.Core;
using RocoPilot.Setup.Views;

namespace RocoPilot.Setup;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly InstallSession _session = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private WelcomeView? _welcome;
    private InstallOptionsView? _options;
    private ProgressView? _progress;
    private CompleteView? _complete;

    private enum Step
    {
        Welcome,
        Options,
        Progress,
        Complete,
    }

    private Step _step;

    public MainWindow()
    {
        InitializeComponent();
        _session.Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        ShowStep(Step.Welcome);
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void ShowStep(Step step)
    {
        _step = step;
        BackButton.Visibility = step is Step.Options or Step.Progress ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = step is not (Step.Progress or Step.Complete);

        switch (step)
        {
            case Step.Welcome:
                _welcome ??= new WelcomeView(_session.Version);
                PageHost.Content = _welcome;
                NextButton.Content = "下一步";
                NextButton.IsEnabled = true;
                break;
            case Step.Options:
                _options ??= new InstallOptionsView(_session);
                PageHost.Content = _options;
                NextButton.Content = "安装";
                NextButton.IsEnabled = true;
                break;
            case Step.Progress:
                _progress ??= new ProgressView();
                PageHost.Content = _progress;
                NextButton.Content = "完成";
                NextButton.IsEnabled = false;
                break;
            case Step.Complete:
                PageHost.Content = _complete;
                BackButton.Visibility = Visibility.Collapsed;
                NextButton.Content = "完成";
                NextButton.IsEnabled = true;
                break;
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case Step.Welcome:
                ShowStep(Step.Options);
                break;
            case Step.Options:
                _options!.Commit();
                ShowStep(Step.Progress);
                RunInstallAsync();
                break;
            case Step.Complete:
                if (_session.LaunchOnExit && _completeShownSuccess)
                {
                    LaunchApp();
                }
                Close();
                break;
        }
    }

    private bool _completeShownSuccess;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step == Step.Options)
        {
            ShowStep(Step.Welcome);
        }
        else if (_step == Step.Progress)
        {
            ShowStep(Step.Options);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void RunInstallAsync()
    {
        try
        {
            await Task.Run(() => Install());
            _completeShownSuccess = true;
            _complete ??= new CompleteView();
            _complete.ShowSuccess(
                $"RocoPilot {_session.Version} 已安装到 {_session.InstallPath}",
                "安装顺利完成。你可以通过开始菜单或桌面快捷方式启动 RocoPilot。");
            ShowStep(Step.Complete);
        }
        catch (Exception ex)
        {
            _complete ??= new CompleteView();
            _complete.ShowFailure($"安装过程中出现问题：{ex.Message}");
            ShowStep(Step.Complete);
        }
    }

    private void Install()
    {
        Progress("正在关闭运行中的应用…");
        AppProcess.Terminate();

        Progress("正在解压应用文件…");
        ExtractEmbeddedPayload(_session.InstallPath);

        Progress("正在创建快捷方式…");
        var appExe = Path.Combine(_session.InstallPath, "RocoPilot.exe");
        ShortcutService.Create(
            InstallLayout.StartMenuShortcutPath,
            appExe,
            _session.InstallPath,
            appExe);
        if (_session.CreateDesktopShortcut)
        {
            ShortcutService.Create(
                InstallLayout.DesktopShortcutPath,
                appExe,
                _session.InstallPath,
                appExe);
        }

        Progress("正在写入卸载信息…");
        RegistryContract.WriteUninstallEntry(_session.Version, _session.InstallPath);
    }

    private static void ExtractEmbeddedPayload(string destination)
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("RocoPilotAppPayload");
        if (stream is null)
        {
            throw new InvalidOperationException("安装器中未包含应用负载。");
        }

        Directory.CreateDirectory(destination);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var entryPath = Path.Combine(destination, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(entryPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
            if (File.Exists(entryPath))
            {
                try
                {
                    File.Delete(entryPath);
                }
                catch
                {
                }
            }

            entry.ExtractToFile(entryPath, overwrite: true);
        }
    }

    private void Progress(string message)
    {
        _dispatcher.Invoke(() => _progress!.SetStatus(message));
    }

    private void LaunchApp()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(_session.InstallPath, "RocoPilot.exe"),
            UseShellExecute = true,
        });
    }
}