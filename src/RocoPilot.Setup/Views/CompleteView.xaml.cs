using System.Windows.Controls;

namespace RocoPilot.Setup.Views;

public sealed partial class CompleteView : UserControl
{
    public CompleteView()
    {
        InitializeComponent();
    }

    public void ShowSuccess(string summary, string message)
    {
        SummaryText.Text = summary;
        MessageText.Text = message;
    }

    public void ShowFailure(string message)
    {
        SummaryText.Text = "安装失败";
        MessageText.Text = message;
    }
}