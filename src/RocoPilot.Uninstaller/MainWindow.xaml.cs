using System.IO;
using System.Windows;
using System.Windows.Threading;
using RocoPilot.Installer.Core;
using RocoPilot.Uninstaller.Views;

namespace RocoPilot.Uninstaller;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private ConfirmView? _confirm;
    private ProgressView? _progress;
    private CompleteView? _complete;

    private enum Step
    {
        Confirm,
        Progress,
        Complete,
    }

    private Step _step;

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        _confirm = new ConfirmView(version);
        ShowStep(Step.Confirm);
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        DragMove();
    }

    private void ShowStep(Step step)
    {
        _step = step;
        CancelButton.Visibility = step == Step.Confirm ? Visibility.Visible : Visibility.Collapsed;
        UninstallButton.Visibility = step == Step.Confirm ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Visibility = step == Step.Complete ? Visibility.Visible : Visibility.Collapsed;

        switch (step)
        {
            case Step.Confirm:
                PageHost.Content = _confirm;
                break;
            case Step.Progress:
                _progress ??= new ProgressView();
                PageHost.Content = _progress;
                break;
            case Step.Complete:
                PageHost.Content = _complete;
                break;
        }
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        _deleteUserData = _confirm!.DeleteUserData;
        _uninstallDriver = _confirm.UninstallDriver;
        ShowStep(Step.Progress);
        RunUninstallAsync();
    }

    private bool _deleteUserData;

    private bool _uninstallDriver;

    private async void RunUninstallAsync()
    {
        try
        {
            await Task.Run(() => Uninstall());
            _complete ??= new CompleteView();
            var message = "RocoPilot 已从你的电脑中移除。";
            if (_uninstallDriver)
            {
                message += "\n\n已卸载 Interception 内核驱动，请重启电脑以完成驱动的彻底移除。";
            }

            _complete.ShowSuccess(message);
            ShowStep(Step.Complete);
        }
        catch (Exception ex)
        {
            _complete ??= new CompleteView();
            _complete.ShowFailure($"卸载过程中出现问题：{ex.Message}");
            ShowStep(Step.Complete);
        }
    }

    private void Uninstall()
    {
        Progress("正在停止应用…");
        AppProcess.Terminate();

        Progress("正在删除快捷方式…");
        ShortcutService.DeleteIfExists(InstallLayout.StartMenuShortcutPath);
        ShortcutService.DeleteIfExists(InstallLayout.DesktopShortcutPath);

        Progress("正在删除应用文件…");
        DeleteDirectory(InstallLayout.InstallRoot);

        Progress("正在清理注册表…");
        RegistryContract.RemoveUninstallEntry();

        if (_uninstallDriver)
        {
            InterceptionDriverHelper.Uninstall(Progress);
        }

        if (_deleteUserData)
        {
            Progress("正在删除个人数据和应用内设置…");
            DeleteDirectory(UserDataPaths.RoamingDataRoot);
            DeleteDirectory(UserDataPaths.LocalDataRoot);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch
            {
            }
        }

        foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories).Reverse())
        {
            try
            {
                Directory.Delete(dir);
            }
            catch
            {
            }
        }

        Directory.Delete(path, recursive: true);
    }

    private void Progress(string message)
    {
        _dispatcher.Invoke(() => _progress!.SetStatus(message));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}