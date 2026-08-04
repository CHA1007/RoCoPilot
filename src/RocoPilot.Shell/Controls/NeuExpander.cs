using System.Windows;
using System.Windows.Controls;

namespace RocoPilot.Shell.Controls;

public class NeuExpander : ContentControl
{
    static NeuExpander()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NeuExpander), new FrameworkPropertyMetadata(typeof(NeuExpander)));
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(NeuExpander));

    public object? Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    public static readonly DependencyProperty TrailingProperty =
        DependencyProperty.Register(nameof(Trailing), typeof(object), typeof(NeuExpander));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(NeuExpander), new PropertyMetadata(false));
}
