using System.Windows;
using System.Windows.Controls;

namespace RocoPilot.Shell.Controls;

public sealed class CenteredWrapPanel : Panel
{
    private readonly List<List<UIElement>> _rows = [];
    private readonly List<double> _rowWidths = [];
    private readonly List<double> _rowHeights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        _rows.Clear();
        _rowWidths.Clear();
        _rowHeights.Clear();

        var maxRowWidth = 0d;
        var totalHeight = 0d;
        var row = new List<UIElement>();
        var rowWidth = 0d;
        var rowHeight = 0d;
        var limit = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;

        foreach (UIElement child in Children)
        {
            child.Measure(availableSize);
            var margin = MarginOf(child);
            var width = child.DesiredSize.Width + margin.Left + margin.Right;
            var height = child.DesiredSize.Height + margin.Top + margin.Bottom;

            if (row.Count > 0 && rowWidth + width > limit)
            {
                CommitRow();
            }

            row.Add(child);
            rowWidth += width;
            rowHeight = Math.Max(rowHeight, height);
        }

        CommitRow();
        return new Size(maxRowWidth, totalHeight);

        void CommitRow()
        {
            if (row.Count == 0) return;
            _rows.Add(row);
            _rowWidths.Add(rowWidth);
            _rowHeights.Add(rowHeight);
            maxRowWidth = Math.Max(maxRowWidth, rowWidth);
            totalHeight += rowHeight;
            row = [];
            rowWidth = 0;
            rowHeight = 0;
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        for (var i = 0; i < _rows.Count; i++)
        {
            var x = Math.Max(0, (finalSize.Width - _rowWidths[i]) / 2);
            foreach (var child in _rows[i])
            {
                var margin = MarginOf(child);
                child.Arrange(new Rect(
                    x + margin.Left,
                    y + margin.Top,
                    child.DesiredSize.Width,
                    child.DesiredSize.Height));
                x += child.DesiredSize.Width + margin.Left + margin.Right;
            }

            y += _rowHeights[i];
        }

        return finalSize;
    }

    private static Thickness MarginOf(UIElement child) =>
        child is FrameworkElement element ? element.Margin : default;
}
