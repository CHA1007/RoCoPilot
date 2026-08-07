using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using RocoPilot.Routing;

namespace RocoPilot.Shell.Controls;

public sealed class RouteLineView : UserControl
{
    private const double StationRadius = 10;
    private const double StationX = 32;
    private const double CardLeft = StationX + StationRadius + 12;
    private const double CardSpacing = 12;
    private const double StationTitleOffset = 22;
    private const double BulgeX = 10;
    private const double GhostGap = 46;
    private const double DragThreshold = 4;

    private readonly Canvas _wireLayer = new();
    private readonly StackPanel _rowPanel = new()
    {
        Margin = new Thickness(0, 16, 24, 96),
    };
    private readonly Canvas _overlayLayer = new();

    private readonly Border _loopChip;
    private readonly TextBlock _loopChipText;
    private readonly Grid _ghostStation;
    private readonly Ellipse _ghostRing;
    private readonly Border _emptyCard;

    private readonly List<CardView> _cards = [];
    private readonly List<Line> _segmentLines = [];
    private readonly List<(Ellipse Ring, TextBlock Number)> _stations = [];
    private readonly Path _returnPath = new() { StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Polygon _returnArrow = new() { IsHitTestVisible = false };
    private readonly Line _ghostLine = new() { StrokeThickness = 2, IsHitTestVisible = false };

    private Guid? _selectedNode;
    private CardView? _pressedCard;
    private Point _downPoint;
    private bool _dragging;
    private bool _running;
    private bool _busy;
    private bool _loopEnabled;

    public event Action<RouteNode>? EditRequested;
    public event Action<Guid>? DeleteRequested;
    public event Action<Guid, int>? MoveRequested;
    public event Action<Guid>? RunRequested;
    public event Action? LoopConfigureRequested;
    public event Action<RouteNodeKind>? AddRequested;

    public RouteLineView()
    {
        Focusable = true;
        Background = new SolidColorBrush(Colors.Transparent);

        _returnPath.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");
        _returnArrow.SetResourceReference(Shape.FillProperty, "ControlStrokeColorDefaultBrush");
        _ghostLine.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");

        _loopChipText = new TextBlock { FontSize = 11, Padding = new Thickness(8, 3, 8, 3) };
        _loopChipText.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        _loopChip = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = _loopChipText,
            Visibility = Visibility.Collapsed,
        };
        _loopChip.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        _loopChip.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _loopChip.MouseLeftButtonUp += (_, e) =>
        {
            if (!_busy) LoopConfigureRequested?.Invoke();
            e.Handled = true;
        };

        _ghostRing = new Ellipse
        {
            Width = StationRadius * 2,
            Height = StationRadius * 2,
            StrokeDashArray = [3, 2],
            StrokeThickness = 1.5,
        };
        _ghostRing.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");
        var ghostPlus = new TextBlock
        {
            Text = "+",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ghostPlus.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        _ghostStation = new Grid
        {
            Width = StationRadius * 2,
            Height = StationRadius * 2,
            Cursor = Cursors.Hand,
        };
        _ghostStation.Children.Add(_ghostRing);
        _ghostStation.Children.Add(ghostPlus);
        _ghostStation.MouseLeftButtonUp += (_, e) =>
        {
            if (_busy) return;
            OpenAddMenu(_ghostStation);
            e.Handled = true;
        };
        _ghostStation.MouseEnter += (_, _) => _ghostStation.Opacity = 0.7;
        _ghostStation.MouseLeave += (_, _) => _ghostStation.Opacity = _busy ? 0.35 : 1.0;

        _emptyCard = BuildEmptyCard();

        var root = new Grid();
        root.Children.Add(_wireLayer);
        root.Children.Add(_rowPanel);
        root.Children.Add(_overlayLayer);
        _overlayLayer.Children.Add(_loopChip);
        _overlayLayer.Children.Add(_ghostStation);
        Content = root;

        LayoutUpdated += (_, _) => UpdateGeometry();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete || _selectedNode is not { } nodeId) return;
            DeleteRequested?.Invoke(nodeId);
            e.Handled = true;
        };
    }

    public void SetSteps(IReadOnlyList<RouteNode> nodes, Guid? activeNodeId = null)
    {
        _rowPanel.Children.Clear();
        _cards.Clear();
        _selectedNode = null;
        _pressedCard = null;
        _dragging = false;

        if (nodes.Count == 0)
        {
            _rowPanel.Children.Add(_emptyCard);
            _rowPanel.Margin = new Thickness(0, 16, 24, 16);
            RebuildWires();
            UpdateGeometry();
            return;
        }

        _rowPanel.Margin = new Thickness(0, 16, 24, 96);

        foreach (var node in nodes)
        {
            var card = BuildCard(node);
            _cards.Add(card);
            _rowPanel.Children.Add(card.Root);
        }

        RebuildWires();
        RefreshCardStates();

        if (activeNodeId is { } id)
            SetActive(id);
    }

    public void SetLoop(bool enabled, int? maxLaps, TimeSpan? maxDuration)
    {
        _loopEnabled = enabled;
        _loopChipText.Text = enabled ? $"↺ {LoopSubtitle(maxLaps, maxDuration)}" : "↺ 已关闭";
        RefreshLoopChipOpacity();
        UpdateGeometry();
    }

    public void SetActive(Guid? nodeId)
    {
        foreach (var card in _cards)
            card.Active = nodeId is { } id && card.Node.Id == id;
        RefreshCardStates();
        RebuildWires();
        UpdateGeometry();
    }

    public void SetRunning(bool running)
    {
        _running = running;
        foreach (var card in _cards)
        {
            if (card.RunButton is { } button)
                button.Content = running ? "■ 停止" : "▶ 从此锚点运行";
        }
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _ghostStation.Opacity = busy ? 0.35 : 1.0;
        RefreshLoopChipOpacity();
    }

    private void RefreshLoopChipOpacity()
        => _loopChip.Opacity = _busy ? 0.6 : (_loopEnabled ? 1.0 : 0.75);

    private CardView BuildCard(RouteNode node)
    {
        var accent = KindAccent(node.Kind);

        var kindLabel = new TextBlock
        {
            Text = KindLabel(node.Kind),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
        };

        var nameLabel = new TextBlock { Text = node.Name, FontSize = 13, FontWeight = FontWeights.SemiBold };
        nameLabel.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var header = new StackPanel();
        header.Children.Add(kindLabel);
        header.Children.Add(nameLabel);

        FrameworkElement contentArea;
        TextBlock? subtitle = null;

        if (node.Kind == RouteNodeKind.Anchor && string.IsNullOrWhiteSpace(node.AnchorName))
        {
            var configure = new Button { Content = "选择魔力之源", MinWidth = 110 };
            configure.SetResourceReference(BackgroundProperty, "AccentFillColorDefaultBrush");
            configure.SetResourceReference(ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            configure.Click += (_, _) => EditRequested?.Invoke(node);
            contentArea = configure;
        }
        else if (node.Kind == RouteNodeKind.Playback && string.IsNullOrWhiteSpace(node.RouteName))
        {
            var configure = new Button { Content = "选择或录制路线", MinWidth = 110 };
            configure.SetResourceReference(BackgroundProperty, "AccentFillColorDefaultBrush");
            configure.SetResourceReference(ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            configure.Click += (_, _) => EditRequested?.Invoke(node);
            contentArea = configure;
        }
        else if (node.Kind == RouteNodeKind.Anchor)
        {
            var run = new Button
            {
                Content = _running ? "■ 停止" : "▶ 从此锚点运行",
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
            };
            run.Click += (_, _) => RunRequested?.Invoke(node.Id);
            contentArea = run;

            subtitle = new TextBlock
            {
                Text = node.AnchorName!,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            subtitle.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        }
        else
        {
            contentArea = new Border { Width = 0 };

            subtitle = new TextBlock
            {
                Text = node.RouteName!,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            subtitle.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        }

        if (subtitle is not null) header.Children.Add(subtitle);

        var deleteButton = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(4, 1, 4, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Hidden,
        };
        deleteButton.Click += (_, _) => DeleteRequested?.Invoke(node.Id);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Margin = new Thickness(14, 10, 8, 10);
        contentArea.Margin = new Thickness(0, 0, 36, 0);
        contentArea.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(header, 0);
        Grid.SetColumn(contentArea, 1);
        Grid.SetColumn(deleteButton, 1);
        layout.Children.Add(header);
        layout.Children.Add(contentArea);
        layout.Children.Add(deleteButton);

        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.SizeNS,
            Child = layout,
            Margin = new Thickness(CardLeft, 0, 0, CardSpacing),
        };
        root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var card = new CardView(node, root) { RunButton = contentArea as Button };

        root.MouseEnter += (_, _) =>
        {
            card.Hovered = true;
            deleteButton.Visibility = Visibility.Visible;
            ApplyCardState(card);
        };
        root.MouseLeave += (_, _) =>
        {
            card.Hovered = false;
            deleteButton.Visibility = Visibility.Hidden;
            ApplyCardState(card);
        };

        root.MouseLeftButtonDown += (_, e) =>
        {
            Keyboard.Focus(this);
            _pressedCard = card;
            _dragging = false;
            _downPoint = e.GetPosition(_rowPanel);
            root.CaptureMouse();
            e.Handled = true;
        };

        root.MouseMove += (_, e) =>
        {
            if (_pressedCard != card || !root.IsMouseCaptured) return;
            var point = e.GetPosition(_rowPanel);
            if (!_dragging)
            {
                if (Math.Abs(point.Y - _downPoint.Y) < DragThreshold) return;
                _dragging = true;
                root.Opacity = 0.8;
                root.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.35 };
            }

            ReorderToward(card, point.Y);
        };

        root.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressedCard != card) return;
            root.ReleaseMouseCapture();
            _pressedCard = null;
            root.Opacity = 1.0;

            if (_dragging)
            {
                _dragging = false;
                root.Effect = null;
                MoveRequested?.Invoke(node.Id, _rowPanel.Children.IndexOf(card.Root));
                return;
            }

            if (e.ClickCount >= 2)
            {
                EditRequested?.Invoke(card.Node);
                return;
            }

            _selectedNode = node.Id;
            RefreshCardStates();
        };

        root.MouseRightButtonDown += (_, e) =>
        {
            var menu = new ContextMenu
            {
                PlacementTarget = root,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            };
            var editItem = new MenuItem { Header = "配置…" };
            editItem.Click += (_, _) => EditRequested?.Invoke(card.Node);
            var deleteItem = new MenuItem { Header = "删除步骤" };
            deleteItem.Click += (_, _) => DeleteRequested?.Invoke(node.Id);
            menu.Items.Add(editItem);
            menu.Items.Add(deleteItem);
            menu.IsOpen = true;
            e.Handled = true;
        };

        return card;
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

    private Border BuildEmptyCard()
    {
        var ring = new Ellipse
        {
            Width = 36,
            Height = 36,
            StrokeDashArray = [3, 2],
            StrokeThickness = 1.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ring.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");
        var plus = new TextBlock
        {
            Text = "+",
            FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        plus.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        var station = new Grid { Width = 36, Height = 36 };
        station.Children.Add(ring);
        station.Children.Add(plus);

        var title = new TextBlock { Text = "添加第一个站点", FontSize = 13, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var subtitle = new TextBlock
        {
            Text = "锚点负责传送定位，回放负责重走录制路线；点击选择站点类型，从这里开始你的路线",
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        subtitle.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(subtitle);

        var body = new Grid { Margin = new Thickness(16, 14, 16, 14) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(station, 0);
        Grid.SetColumn(text, 1);
        body.Children.Add(station);
        body.Children.Add(text);

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Child = body,
        };
        card.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        card.MouseEnter += (_, _) => card.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
        card.MouseLeave += (_, _) => card.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (_busy) return;
            OpenAddMenu(card);
            e.Handled = true;
        };
        return card;
    }

    private void ReorderToward(CardView card, double y)
    {
        var currentIndex = _rowPanel.Children.IndexOf(card.Root);
        var targetIndex = 0;
        foreach (var sibling in _cards)
        {
            if (sibling == card) continue;
            var top = sibling.Root.TranslatePoint(new Point(0, 0), _rowPanel).Y;
            if (y > top + sibling.Root.ActualHeight / 2) targetIndex++;
        }

        if (targetIndex == currentIndex) return;

        _rowPanel.Children.Remove(card.Root);
        _rowPanel.Children.Insert(targetIndex, card.Root);
        UpdateGeometry();
    }

    private void RebuildWires()
    {
        _wireLayer.Children.Clear();
        _segmentLines.Clear();
        _stations.Clear();

        for (var i = 0; i < _cards.Count - 1; i++)
        {
            var segment = new Line { X1 = StationX, X2 = StationX, StrokeThickness = 2, IsHitTestVisible = false };
            segment.SetResourceReference(Shape.StrokeProperty, "ControlStrokeColorDefaultBrush");
            _segmentLines.Add(segment);
            _wireLayer.Children.Add(segment);
        }

        _wireLayer.Children.Add(_ghostLine);
        _wireLayer.Children.Add(_returnPath);
        _wireLayer.Children.Add(_returnArrow);

        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            var active = card.Active;
            var ring = new Ellipse
            {
                Width = StationRadius * 2,
                Height = StationRadius * 2,
                StrokeThickness = active ? 2.5 : 1.5,
                IsHitTestVisible = false,
            };
            ring.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");

            var number = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            number.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

            if (active)
            {
                var accent = KindAccent(card.Node.Kind);
                ring.Stroke = new SolidColorBrush(accent);
                ring.Fill = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
                number.Foreground = new SolidColorBrush(accent);
            }

            _wireLayer.Children.Add(ring);
            _wireLayer.Children.Add(number);
            _stations.Add((ring, number));
        }
    }

    private void UpdateGeometry()
    {
        if (_cards.Count != _stations.Count) return;

        var centers = new Point[_cards.Count];
        for (var i = 0; i < _cards.Count; i++)
        {
            var top = _cards[i].Root.TranslatePoint(new Point(0, 0), _wireLayer).Y;
            centers[i] = new Point(StationX, top + StationTitleOffset);
        }

        for (var i = 0; i < _cards.Count; i++)
        {
            var (ring, number) = _stations[i];
            Canvas.SetLeft(ring, centers[i].X - StationRadius);
            Canvas.SetTop(ring, centers[i].Y - StationRadius);
            Canvas.SetLeft(number, centers[i].X - 8);
            Canvas.SetTop(number, centers[i].Y - 8);
            number.Width = 16;
            number.Height = 16;
        }

        for (var i = 0; i < _segmentLines.Count; i++)
        {
            _segmentLines[i].Y1 = centers[i].Y + StationRadius;
            _segmentLines[i].Y2 = centers[i + 1].Y - StationRadius;
        }

        if (_cards.Count == 0)
        {
            _returnPath.Data = null;
            _returnArrow.Points = null;
            _loopChip.Visibility = Visibility.Collapsed;
            _ghostLine.X1 = _ghostLine.X2 = StationX;
            _ghostLine.Y1 = _ghostLine.Y2 = 0;
            _ghostStation.Visibility = Visibility.Collapsed;
            return;
        }

        _ghostStation.Visibility = _busy ? Visibility.Collapsed : Visibility.Visible;
        _ghostLine.Visibility = _busy ? Visibility.Collapsed : Visibility.Visible;

        var first = centers[0];
        var last = centers[^1];

        _ghostLine.X1 = _ghostLine.X2 = StationX;
        _ghostLine.Y1 = last.Y + StationRadius;
        _ghostLine.Y2 = last.Y + GhostGap - StationRadius;
        Canvas.SetLeft(_ghostStation, StationX - StationRadius);
        Canvas.SetTop(_ghostStation, last.Y + GhostGap - StationRadius);

        double loopCenterY;

        if (_cards.Count == 1)
        {
            _returnPath.Data = SingleNodeLoopGeometry(first.Y);
            _returnArrow.Points = null;
            loopCenterY = first.Y;
        }
        else
        {
            _returnPath.Data = ReturnLineGeometry(first.Y, last.Y);
            _returnArrow.Points =
            [
                new Point(StationX - StationRadius - 9, first.Y - 5),
                new Point(StationX - StationRadius - 9, first.Y + 5),
                new Point(StationX - StationRadius - 1, first.Y),
            ];
            loopCenterY = (first.Y + last.Y) / 2;
        }

        _returnPath.StrokeDashArray = _loopEnabled ? null : [4, 3];
        _returnArrow.Visibility = _loopEnabled && _cards.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        _loopChip.Visibility = Visibility.Visible;
        Canvas.SetLeft(_loopChip, 0);
        Canvas.SetTop(_loopChip, loopCenterY - _loopChip.ActualHeight / 2);
    }

    private Geometry ReturnLineGeometry(double firstY, double lastY)
    {
        var startX = StationX - StationRadius;
        return RoundedPolyline(
        [
            new Point(startX, lastY),
            new Point(BulgeX, lastY),
            new Point(BulgeX, firstY),
            new Point(startX, firstY),
        ], 10);
    }

    private static Geometry SingleNodeLoopGeometry(double y)
    {
        var startX = StationX - StationRadius;
        var figure = new PathFigure { StartPoint = new Point(startX, y - 5) };
        figure.Segments.Add(new BezierSegment(
            new Point(BulgeX - 16, y - 22),
            new Point(BulgeX - 16, y + 22),
            new Point(startX, y + 5),
            isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Geometry RoundedPolyline(IReadOnlyList<Point> points, double radius)
    {
        var figure = new PathFigure { StartPoint = points[0] };
        for (var i = 1; i < points.Count - 1; i++)
        {
            var incoming = points[i] - points[i - 1];
            var outgoing = points[i + 1] - points[i];
            if (incoming.Length < 1 || outgoing.Length < 1)
            {
                figure.Segments.Add(new LineSegment(points[i], true));
                continue;
            }

            var cut = Math.Min(radius, Math.Min(incoming.Length, outgoing.Length) / 2);
            incoming.Normalize();
            outgoing.Normalize();
            var cross = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
            figure.Segments.Add(new LineSegment(points[i] - incoming * cut, true));
            figure.Segments.Add(new ArcSegment
            {
                Point = points[i] + outgoing * cut,
                Size = new Size(cut, cut),
                SweepDirection = cross > 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                IsStroked = true,
            });
        }

        figure.Segments.Add(new LineSegment(points[^1], true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void RefreshCardStates()
    {
        foreach (var card in _cards)
            ApplyCardState(card);
    }

    private void ApplyCardState(CardView card)
    {
        var root = card.Root;

        if (card.Active)
        {
            var accent = KindAccent(card.Node.Kind);
            root.BorderBrush = new SolidColorBrush(accent);
            root.BorderThickness = new Thickness(2.5);
            root.Effect = new DropShadowEffect
            {
                Color = accent,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.7,
            };
            return;
        }

        root.Effect = null;
        var selected = card.Node.Id == _selectedNode;
        root.BorderThickness = new Thickness(selected ? 2.5 : 1.5);
        if (selected)
            root.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        else
            root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        if (card.Hovered)
            root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
        else
            root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
    }

    private static Color KindAccent(RouteNodeKind kind) => kind switch
    {
        RouteNodeKind.Anchor => Color.FromRgb(0x4C, 0x8D, 0xF0),
        RouteNodeKind.Playback => Color.FromRgb(0x57, 0xB0, 0x5C),
        _ => Color.FromRgb(0x80, 0x80, 0x80),
    };

    private static string KindLabel(RouteNodeKind kind) => kind switch
    {
        RouteNodeKind.Anchor => "锚点",
        RouteNodeKind.Playback => "回放",
        _ => "?",
    };

    private static string LoopSubtitle(int? maxLaps, TimeSpan? maxDuration)
    {
        if (maxLaps is null && maxDuration is null) return "无限";
        var parts = new List<string>();
        if (maxLaps is { } laps) parts.Add($"≤{laps} 圈");
        if (maxDuration is { } duration) parts.Add($"≤{duration.TotalMinutes:0.#} 分钟");
        return string.Join(" · ", parts);
    }

    private sealed class CardView(RouteNode node, Border root)
    {
        public RouteNode Node { get; } = node;
        public Border Root { get; } = root;
        public Button? RunButton { get; init; }
        public bool Active { get; set; }
        public bool Hovered { get; set; }
    }
}
