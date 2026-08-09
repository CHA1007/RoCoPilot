using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;

namespace RocoPilot.Setup.Views;

public sealed partial class InstallOptionsView : UserControl
{
    private readonly InstallSession _session;

    public InstallOptionsView(InstallSession session)
    {
        InitializeComponent();
        _session = session;
        DataContext = session;
        InstallPathBox.Text = session.InstallPath;
        DesktopShortcutBox.IsChecked = session.CreateDesktopShortcut;
        LaunchOnExitBox.IsChecked = session.LaunchOnExit;
    }

    public void Commit()
    {
        _session.InstallPath = InstallPathBox.Text.Trim();
        _session.CreateDesktopShortcut = DesktopShortcutBox.IsChecked == true;
        _session.LaunchOnExit = LaunchOnExitBox.IsChecked == true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择安装位置",
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
        {
            InstallPathBox.Text = dialog.FolderName;
        }
    }
}