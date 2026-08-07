using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using RocoPilot.Routing;
using RocoPilot.Shell.Appearance;
using UiButton = Wpf.Ui.Controls.Button;
using UiIcon = Wpf.Ui.Controls.SymbolIcon;
using CtlApp = Wpf.Ui.Controls.ControlAppearance;
using Sim = Wpf.Ui.Controls.SymbolRegular;

namespace RocoPilot.Shell.Controls;

public sealed class RouteLineView : UserControl
{
    private const double BadgeSize = 26;
    private const double RowGap = 10;

    private readonly StackPanel _rows;
    private readonly List<RowView> _rowsView = [];
    private readonly Border _addCard;
    private readonly ScrollViewer _scroll;
    private readonly FrameworkElement _empty;

    private Guid? _selectedId;
    private RowView? _pressedRow;
    private Point _downPoint;
    private bool _dragging;
    private bool _busy;

    public event Action<Guid>? SelectRequested;
    public event Action<Guid>? DeleteRequested;
    public event Action<Guid, int>? MoveRequested;
    public event Action<Guid>? RunRequested;
    public event Action<RouteNodeKind>? AddRequested;

    public RouteLineView()
    {
        Focusable = true;
        Background = new SolidColorBrush(Colors.Transparent);

        _addCard = BuildAddCard();
        _rows = new StackPanel();
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rows,
        };
        _empty = BuildEmptyState();

        var root = new Grid();
        root.Children.Add(_scroll);
        root.Children.Add(_empty);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete || _selectedId is not { } id) return;
            DeleteRequested?.Invoke(id);
            e.Handled = true;
        };
    }

    public void SetSteps(IReadOnlyList<RouteNode> nodes, Guid? activeNodeId = null)
    {
        _rows.Children.Clear();
        _rowsView.Clear();
        _selectedId = null;
        _pressedRow = null;
        _dragging = false;

        for (var i = 0; i < nodes.Count; i++)
        {
            var row = BuildCard(nodes[i], i);
            _rowsView.Add(row);
            _rows.Children.Add(row.Root);
        }

        _rows.Children.Add(_addCard);

        var hasSteps = nodes.Count > 0;
        _scroll.Visibility = hasSteps ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = hasSteps ? Visibility.Collapsed : Visibility.Visible;

        RefreshStates();

        if (activeNodeId is { } id)
            SetActive(id);
    }

    public void SetLoop(bool enabled, int? maxLaps, TimeSpan? maxDuration)
    {
    }

    public void SetActive(Guid? nodeId)
    {
        foreach (var row in _rowsView)
            row.Active = nodeId is { } id && row.Node.Id == id;
        RefreshStates();
    }

    public void SetSelected(Guid? nodeId)
    {
        _selectedId = nodeId;
        RefreshStates();
    }

    public void SetRunning(bool running)
    {
        foreach (var row in _rowsView)
        {
            if (row.RunButton is { } button)
            {
                button.Content = running ? "停止" : "运行";
                button.Icon = new UiIcon
                {
                    Symbol = running ? Sim.Stop20 : Sim.Play20,
                    FontSize = 14,
                    Width = 14,
                    Height = 14,
                };
            }
        }
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
    }

    private RowView BuildCard(RouteNode node, int index)
    {
        // ---- 序号徽章 ----
        var badge = new Ellipse
        {
            Width = BadgeSize,
            Height = BadgeSize,
            StrokeThickness = 1.5,
            Fill = RouteVisuals.AccentSoftBrush,
            Stroke = RouteVisuals.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var number = new TextBlock
        {
            Text = (index + 1).ToString(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = RouteVisuals.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var badgeHost = new Grid { Width = BadgeSize, Height = BadgeSize, VerticalAlignment = VerticalAlignment.Center };
        badgeHost.Children.Add(badge);
        badgeHost.Children.Add(number);

        // ---- 类型图标 ----
        var glyph = new UiIcon
        {
            Symbol = IconFor(node.Kind),
            FontSize = 16,
            Width = 16,
            Height = 16,
            Foreground = RouteVisuals.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };

        // ---- 名称 + 元信息 ----
        var metaText = node.Kind == RouteNodeKind.Anchor ? node.AnchorName : node.RouteName;
        var configured = !string.IsNullOrWhiteSpace(metaText);

        var name = new TextBlock
        {
            Text = node.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = node.Name,
        };
        name.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(name);

        if (!configured)
        {
            var meta = new TextBlock
            {
                Text = "待配置",
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
            };
            meta.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");
            info.Children.Add(meta);
        }

        // ---- 操作区 ----
        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(14, 0, 0, 0),
        };

        UiButton runButton;
        if (configured)
        {
            runButton = new UiButton
            {
                Content = "运行",
                Icon = new UiIcon { Symbol = Sim.Play20, FontSize = 14, Width = 14, Height = 14 },
                Appearance = CtlApp.Secondary,
                Background = Brushes.Transparent,
                Foreground = RouteVisuals.AccentBrush,
                MouseOverBackground = RouteVisuals.AccentGhostHoverBrush,
                PressedBackground = RouteVisuals.AccentGhostPressedBrush,
                BorderBrush = RouteVisuals.AccentBrush,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "从此步骤运行",
            };
            runButton.Click += (_, _) => RunRequested?.Invoke(node.Id);
        }
        else
        {
            runButton = new UiButton
            {
                Content = KindAction(node.Kind),
                Icon = new UiIcon
                {
                    Symbol = node.Kind == RouteNodeKind.Anchor ? Sim.Location20 : Sim.FolderOpen20,
                    FontSize = 14,
                    Width = 14,
                    Height = 14,
                },
                Appearance = CtlApp.Secondary,
                Background = Brushes.Transparent,
                Foreground = RouteVisuals.AccentBrush,
                MouseOverBackground = RouteVisuals.AccentGhostHoverBrush,
                PressedBackground = RouteVisuals.AccentGhostPressedBrush,
                BorderBrush = RouteVisuals.AccentBrush,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                Padding = new Thickness(12, 5, 12, 5),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            runButton.Click += (_, _) => SelectRequested?.Invoke(node.Id);
        }

        var deleteButton = new UiButton
        {
            Content = string.Empty,
            Icon = new UiIcon { Symbol = Sim.Dismiss20, FontSize = 14, Width = 14, Height = 14 },
            Appearance = CtlApp.Transparent,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "删除步骤",
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden,
        };
        deleteButton.SetResourceReference(Control.ForegroundProperty, "SystemFillColorCriticalBrush");
        deleteButton.Click += (_, _) => DeleteRequested?.Invoke(node.Id);

        ops.Children.Add(runButton);
        ops.Children.Add(deleteButton);

        // ---- 行布局 ----
        var content = new Grid { Margin = new Thickness(0, 12, 14, 12) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(badgeHost, 0);
        Grid.SetColumn(glyph, 1);
        Grid.SetColumn(info, 2);
        Grid.SetColumn(ops, 3);
        content.Children.Add(badgeHost);
        content.Children.Add(glyph);
        content.Children.Add(info);
        content.Children.Add(ops);

        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Child = content,
            Margin = new Thickness(0, 0, 0, RowGap),
        };
        root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var row = new RowView(node, root) { RunButton = configured ? runButton : null };

        root.MouseEnter += (_, _) =>
        {
            row.Hovered = true;
            deleteButton.Visibility = Visibility.Visible;
            ApplyRowState(row);
        };
        root.MouseLeave += (_, _) =>
        {
            row.Hovered = false;
            deleteButton.Visibility = Visibility.Hidden;
            ApplyRowState(row);
        };

        root.MouseLeftButtonDown += (_, e) =>
        {
            if (_busy) return;
            Keyboard.Focus(this);
            _pressedRow = row;
            _dragging = false;
            _downPoint = e.GetPosition(_rows);
            root.CaptureMouse();
            e.Handled = true;
        };

        root.MouseMove += (_, e) =>
        {
            if (_pressedRow != row || !root.IsMouseCaptured) return;
            var point = e.GetPosition(_rows);
            if (!_dragging)
            {
                if (Math.Abs(point.Y - _downPoint.Y) < 5) return;
                _dragging = true;
                root.Opacity = 0.85;
                root.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 };
            }

            ReorderToward(row, point.Y);
        };

        root.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressedRow != row) return;
            root.ReleaseMouseCapture();
            _pressedRow = null;
            root.Opacity = 1.0;

            if (_dragging)
            {
                _dragging = false;
                root.Effect = null;
                MoveRequested?.Invoke(node.Id, _rows.Children.IndexOf(row.Root));
                return;
            }

            _selectedId = node.Id;
            RefreshStates();
            SelectRequested?.Invoke(node.Id);
        };

        root.MouseRightButtonDown += (_, e) =>
        {
            var menu = new ContextMenu
            {
                PlacementTarget = root,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };
            var configItem = new MenuItem { Header = "在右侧配置…" };
            configItem.Click += (_, _) => SelectRequested?.Invoke(node.Id);
            var deleteItem = new MenuItem { Header = "删除步骤" };
            deleteItem.Click += (_, _) => DeleteRequested?.Invoke(node.Id);
            var upItem = new MenuItem { Header = "上移" };
            upItem.Click += (_, _) => MoveRequested?.Invoke(node.Id, _rows.Children.IndexOf(row.Root) - 1);
            var downItem = new MenuItem { Header = "下移" };
            downItem.Click += (_, _) => MoveRequested?.Invoke(node.Id, _rows.Children.IndexOf(row.Root) + 1);
            menu.Items.Add(configItem);
            menu.Items.Add(deleteItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(upItem);
            menu.Items.Add(downItem);
            menu.IsOpen = true;
            e.Handled = true;
        };

        return row;
    }

    private FrameworkElement BuildEmptyState()
    {
        var icon = new UiIcon
        {
            Symbol = Sim.Add24,
            FontSize = 34,
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFillColorTertiaryBrush");

        var title = new TextBlock
        {
            Text = "还没有步骤",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var hint = new TextBlock
        {
            Text = "从添加一个锚点或回放开始编排你的路线",
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hint.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var add = new UiButton
        {
            Content = "添加第一个步骤",
            Icon = new UiIcon { Symbol = Sim.Add20, FontSize = 14, Width = 14, Height = 14 },
            Appearance = CtlApp.Secondary,
            Background = Brushes.Transparent,
            Foreground = RouteVisuals.AccentBrush,
            BorderBrush = RouteVisuals.AccentBrush,
            BorderThickness = new Thickness(1),
            MouseOverBackground = RouteVisuals.AccentGhostHoverBrush,
            PressedBackground = RouteVisuals.AccentGhostPressedBrush,
            FontSize = 12,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        add.Click += (_, _) => OpenAddMenu(add);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(icon);
        content.Children.Add(title);
        content.Children.Add(hint);
        content.Children.Add(add);

        var host = new Grid { VerticalAlignment = VerticalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        host.Children.Add(content);
        return host;
    }

    private Border BuildAddCard()
    {
        var frame = new Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent,
            Stretch = Stretch.Fill,
        };
        frame.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");

        var icon = new UiIcon
        {
            Symbol = Sim.Add24,
            FontSize = 18,
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        icon.SetResourceReference(Control.ForegroundProperty, "TextFillColorSecondaryBrush");

        var text = new TextBlock
        {
            Text = "添加步骤",
            FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        text.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var inner = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 14, 16, 14),
        };
        inner.Children.Add(icon);
        inner.Children.Add(text);

        var grid = new Grid
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        grid.Children.Add(frame);
        grid.Children.Add(inner);

        grid.MouseLeftButtonUp += (_, e) =>
        {
            if (_busy) return;
            OpenAddMenu(grid);
            e.Handled = true;
        };
        grid.MouseEnter += (_, _) =>
        {
            if (_busy) return;
            frame.Stroke = RouteVisuals.AccentBrush;
            frame.StrokeThickness = 2;
        };
        grid.MouseLeave += (_, _) =>
        {
            frame.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");
            frame.StrokeThickness = 1.5;
        };

        return new Border
        {
            Child = grid,
            Margin = new Thickness(0, 4, 0, RowGap),
        };
    }

    private void OpenAddMenu(FrameworkElement placementTarget)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };
        var anchorItem = new MenuItem { Header = "锚点（传送引导）" };
        anchorItem.Click += (_, _) => AddRequested?.Invoke(RouteNodeKind.Anchor);
        var playbackItem = new MenuItem { Header = "回放（重放录制路线）" };
        playbackItem.Click += (_, _) => AddRequested?.Invoke(RouteNodeKind.Playback);
        menu.Items.Add(anchorItem);
        menu.Items.Add(playbackItem);
        menu.IsOpen = true;
    }

    private void ReorderToward(RowView row, double y)
    {
        var currentIndex = _rows.Children.IndexOf(row.Root);
        var targetIndex = 0;
        foreach (var sibling in _rowsView)
        {
            if (sibling == row) continue;
            var top = sibling.Root.TranslatePoint(new Point(0, 0), _rows).Y;
            if (y > top + sibling.Root.ActualHeight / 2) targetIndex++;
        }

        if (targetIndex == currentIndex) return;

        _rows.Children.Remove(row.Root);
        _rows.Children.Insert(targetIndex, row.Root);
    }

    private void RefreshStates()
    {
        foreach (var row in _rowsView)
            ApplyRowState(row);
    }

    private void ApplyRowState(RowView row)
    {
        var root = row.Root;

        if (row.Active)
        {
            root.BorderBrush = RouteVisuals.AccentBrush;
            root.BorderThickness = new Thickness(2.5);
            root.Effect = new DropShadowEffect
            {
                Color = RouteVisuals.Accent,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.65,
            };
            return;
        }

        root.Effect = null;
        var selected = row.Node.Id == _selectedId;
        root.BorderThickness = new Thickness(selected ? 2.5 : 1.5);
        if (selected)
            root.BorderBrush = RouteVisuals.AccentBrush;
        else if (row.Hovered)
            root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorSecondaryBrush");
        else
            root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        if (row.Hovered)
            root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
        else
            root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
    }

    private static Sim IconFor(RouteNodeKind kind) => kind switch
    {
        RouteNodeKind.Anchor => Sim.Location20,
        RouteNodeKind.Playback => Sim.Play20,
        _ => Sim.QuestionCircle20,
    };

    private static string KindAction(RouteNodeKind kind) =>
        kind == RouteNodeKind.Anchor ? "选择魔力之源" : "选择或录制路线";

    private sealed class RowView(RouteNode node, Border root)
    {
        public RouteNode Node { get; } = node;
        public Border Root { get; } = root;
        public UiButton? RunButton { get; init; }
        public bool Active { get; set; }
        public bool Hovered { get; set; }
    }
}