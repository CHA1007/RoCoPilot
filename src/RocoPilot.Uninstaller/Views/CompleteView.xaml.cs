using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RocoPilot.Installer.Core;

namespace RocoPilot.Uninstaller.Views;

public sealed partial class CompleteView : UserControl
{
    public CompleteView()
    {
        InitializeComponent();
    }

    public void ShowSuccess(string message)
    {
        TitleText.Text = "卸载完成";
        MessageText.Text = message;
    }

    public void ShowFailure(string message)
    {
        TitleText.Text = "卸载失败";
        MessageText.Text = message;
        ResultIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle24;
        ResultIcon.Foreground = (Brush)FindResource("SystemFillColorCriticalBrush");
    }
}