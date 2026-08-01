using System.Windows;
using System.Windows.Controls;
using RocoPilot.Input;

namespace RocoPilot.Tools.AutoThrow;

public partial class AutoThrowConfigPanel : UserControl
{
    private readonly AutoThrowSettings _settings;
    private readonly Action _persist;
    private bool _ready;

    public AutoThrowConfigPanel(AutoThrowSettings settings, Action persist)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        _settings = settings;
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));

        _settings.SanitizeInPlace();
        DataContext = _settings;
        RebuildWhitelistRows();
        Loaded += (_, _) => _ready = true;
    }

    private void OnSourceUpdated(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (_ready)
        {
            Commit();
        }
    }

    private void OnAddWhitelistClick(object sender, RoutedEventArgs e) =>
        WhitelistHost.Children.Add(CreateWhitelistRow(string.Empty));

    private void OnWhitelistRowCommitted(object sender, RoutedEventArgs e) => CommitWhitelist();

    private void CommitWhitelist()
    {
        _settings.DetectionWhitelist = [.. RowTexts()];
        Commit();
        if (!RowsMatchSettings())
        {
            RebuildWhitelistRows();
        }
    }

    private void Commit()
    {
        _settings.SanitizeInPlace();
        _persist();
    }

    private void RebuildWhitelistRows()
    {
        WhitelistHost.Children.Clear();
        foreach (var name in _settings.DetectionWhitelist ?? [])
        {
            WhitelistHost.Children.Add(CreateWhitelistRow(name));
        }
    }

    private Grid CreateWhitelistRow(string name)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new Wpf.Ui.Controls.TextBox { Text = name };
        box.LostFocus += OnWhitelistRowCommitted;
        Grid.SetColumn(box, 0);

        var remove = new Wpf.Ui.Controls.Button { Content = "移除", Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) =>
        {
            WhitelistHost.Children.Remove(row);
            CommitWhitelist();
        };
        Grid.SetColumn(remove, 1);

        row.Children.Add(box);
        row.Children.Add(remove);
        return row;
    }

    private List<string> RowTexts() =>
        WhitelistHost.Children.OfType<Grid>()
            .Select(grid => grid.Children.OfType<Wpf.Ui.Controls.TextBox>().FirstOrDefault()?.Text ?? string.Empty)
            .ToList();

    private bool RowsMatchSettings()
    {
        var expected = _settings.DetectionWhitelist ?? [];
        var rows = RowTexts();
        return rows.Count == expected.Length && rows.SequenceEqual(expected);
    }
}
