using System.Windows.Controls;
using RocoPilot.Installer.Core;

namespace RocoPilot.Uninstaller.Views;

public sealed partial class ConfirmView : UserControl
{
    public ConfirmView(string version)
    {
        InitializeComponent();
        DetailText.Text = $"版本 {version}";
        UninstallDriverBox.IsEnabled = InterceptionDriverHelper.IsInstalled();
    }

    public bool DeleteUserData => DeleteUserDataBox.IsChecked == true;

    public bool UninstallDriver => UninstallDriverBox.IsChecked == true;
}