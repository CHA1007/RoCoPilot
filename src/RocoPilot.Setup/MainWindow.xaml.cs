using System.Diagnostics;
using System.IO;
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
    private bool _driverInstalled;

    public MainWindow()
    {
        InitializeComponent();
        _session.Version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        _session.InterceptionMissing = !InterceptionDriverHelper.IsInstalled();
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
            if (_complete is null)
            {
                _complete = new CompleteView();
                _complete.LaunchRequested += () =>
                {
                    LaunchApp();
                    Close();
                };
            }

            _complete.ShowSuccess(
                $"版本 {_session.Version} · 已安装到 {_session.InstallPath}",
                "安装顺利完成，可通过开始菜单或桌面快捷方式启动 RocoPilot。");
            if (_driverInstalled)
            {
                _complete.ShowDriverRebootNote();
            }

            ShowStep(Step.Complete);

            if (_driverInstalled)
            {
                PromptReboot();
            }
        }
        catch (Exception ex)
        {
            _complete ??= new CompleteView();
            _complete.ShowFailure($"安装过程中出现问题：{ex.Message}");
            ShowStep(Step.Complete);
        }
    }

    private void PromptReboot()
    {
        var choice = MessageBox.Show(
            "Interception 内核驱动已安装，需要重启电脑后才能生效。是否立即重启？",
            "RocoPilot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes)
        {
            var psi = new ProcessStartInfo("shutdown", "/r /t 0")
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法重启电脑：{ex.Message}", "RocoPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void Install()
    {
        Progress("正在关闭运行中的应用…");
        AppProcess.Terminate();

        Progress("正在准备安装程序…");
        var setupExe = ExtractEmbeddedSetup();

        try
        {
            Progress("正在安装 RocoPilot…");
            var log = Path.Combine(Path.GetTempPath(), "RocoPilot-setup.log");
            var psi = new ProcessStartInfo
            {
                FileName = setupExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"--silent --installto \"{_session.InstallPath}\" --log \"{log}\"",
            };
            using var setup = Process.Start(psi) ?? throw new InvalidOperationException("无法启动安装程序。");
            setup.WaitForExit();
            if (setup.ExitCode != 0)
            {
                throw new InvalidOperationException($"安装程序失败（退出码 {setup.ExitCode}）。");
            }
        }
        finally
        {
            TryDelete(setupExe);
        }

        if (!_session.CreateDesktopShortcut)
        {
            Progress("正在整理快捷方式…");
            ShortcutService.DeleteIfExists(InstallLayout.DesktopShortcutPath);
        }

        Progress("正在写入卸载信息…");
        RegistryContract.WriteUninstallEntry(_session.Version, Path.Combine(_session.InstallPath, "current"));

        if (_session.InstallInterceptionDriver)
        {
            InstallInterceptionDriver();
        }
    }

    private void InstallInterceptionDriver()
    {
        Progress("正在安装 Interception 内核驱动…");
        try
        {
            InterceptionDriverHelper.InstallAsync(Progress).GetAwaiter().GetResult();
            _driverInstalled = true;
            Progress("内核驱动安装完成，重启后生效。");
        }
        catch (Exception ex)
        {
            Progress($"内核驱动安装未完成：{ex.GetBaseException().Message}");
        }
    }

    private static string ExtractEmbeddedSetup()
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("RocoPilotSetupPayload");
        if (stream is null)
        {
            throw new InvalidOperationException("安装器中未内置安装程序负载。");
        }

        var dest = Path.Combine(Path.GetTempPath(), $"RocoPilot-setup-{Guid.NewGuid():N}.exe");
        using (var file = File.Create(dest))
        {
            stream.CopyTo(file);
        }

        return dest;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
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