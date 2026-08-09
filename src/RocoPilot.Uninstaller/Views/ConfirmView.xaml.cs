using System.Windows.Controls;

namespace RocoPilot.Uninstaller.Views;

public sealed partial class ConfirmView : UserControl
{
    public ConfirmView(string version)
    {
        InitializeComponent();
        DetailText.Text = $"版本 {version}";
    }

    public bool DeleteUserData => DeleteUserDataBox.IsChecked == true;
}