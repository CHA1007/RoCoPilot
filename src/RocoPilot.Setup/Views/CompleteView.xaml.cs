using System.Windows;
using System.Windows.Controls;

namespace RocoPilot.Setup.Views;

public sealed partial class CompleteView : UserControl
{
    public CompleteView()
    {
        InitializeComponent();
    }

    public event Action? LaunchRequested;

    public void ShowSuccess(string summary, string message)
    {
        SummaryText.Text = summary;
        MessageText.Text = message;
    }

    public void ShowDriverRebootNote()
    {
        RebootNote.Visibility = Visibility.Visible;
        RebootText.Text = "已安装 Interception 内核驱动，重启电脑后才生效。";
    }

    public void ShowFailure(string message)
    {
        SummaryText.Text = "安装失败";
        MessageText.Text = message;
        LaunchButton.Visibility = Visibility.Collapsed;
        RebootNote.Visibility = Visibility.Collapsed;
    }

    private void Launch_Click(object sender, RoutedEventArgs e) => LaunchRequested?.Invoke();
}