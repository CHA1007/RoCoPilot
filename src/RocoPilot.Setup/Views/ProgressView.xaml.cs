using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RocoPilot.Setup.Views;

public sealed partial class ProgressView : UserControl
{
    public ProgressView()
    {
        InitializeComponent();
        Loaded += (_, _) => StartIndeterminate();
    }

    public void SetStatus(string message) => StatusText.Text = message;

    private void StartIndeterminate()
    {
        var trackWidth = Track.ActualWidth;
        if (trackWidth <= 0) return;

        var animation = new DoubleAnimation
        {
            From = -Indicator.Width,
            To = trackWidth,
            Duration = TimeSpan.FromSeconds(1.4),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
