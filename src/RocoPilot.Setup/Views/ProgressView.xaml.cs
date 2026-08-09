using System.Windows.Controls;

namespace RocoPilot.Setup.Views;

public sealed partial class ProgressView : UserControl
{
    public ProgressView()
    {
        InitializeComponent();
    }

    public void SetStatus(string message)
    {
        StatusText.Text = message;
    }
}