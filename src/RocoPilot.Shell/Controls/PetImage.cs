using System.Windows;
using System.Windows.Controls;

namespace RocoPilot.Shell.Controls;

public sealed class PetImage : Image
{
    public static readonly DependencyProperty SourceUrlProperty = DependencyProperty.Register(
        nameof(SourceUrl), typeof(string), typeof(PetImage), new PropertyMetadata(null, OnSourceUrlChanged));

    public string? SourceUrl
    {
        get => (string?)GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    public static readonly DependencyProperty HasImageProperty = DependencyProperty.Register(
        nameof(HasImage), typeof(bool), typeof(PetImage), new PropertyMetadata(false));

    public bool HasImage
    {
        get => (bool)GetValue(HasImageProperty);
        private set => SetValue(HasImageProperty, value);
    }

    private static void OnSourceUrlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PetImage)d).LoadAsync((string?)e.NewValue);

    private async void LoadAsync(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Source = null;
            HasImage = false;
            return;
        }

        var image = await PetImageLoader.GetOrCreateAsync(url);
        if (url == SourceUrl)
        {
            Source = image;
            HasImage = image is not null;
        }
    }
}
