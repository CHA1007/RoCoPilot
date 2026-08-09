using System.Windows.Controls;

namespace RocoPilot.Setup.Views;

public sealed partial class WelcomeView : UserControl
{
    public WelcomeView(string version)
    {
        InitializeComponent();
        VersionText.Text = $"版本 {version}";
    }
}