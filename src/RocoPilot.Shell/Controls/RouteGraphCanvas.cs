using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RocoPilot.Routing;

namespace RocoPilot.Shell.Controls;

public sealed class RouteGraphCanvas : UserControl
{
    private const double CardWidth = 168;
    private const double PortRadius = 6;
    private const double PortHitRadius = 14;
    private const double DragThreshold = 4;

    private readonly Canvas _wireLayer = new();
    private readonly Canvas _nodeLayer = new();
    private readonly Canvas _overlayLayer = new();
    private readonly TextBlock _emptyHint = new()
    {
        Text = "还没有节点——用上方工具栏添加锚点 / 回放 / 循环节点，再从节点右侧端口拖出连线",
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 420,
        TextAlignment = TextAlignment.Center,
        IsHitTestVisible = false,
    };

    private readonly Dictionary<Guid, CardView> _cards = [];
    private readonly List<EdgeView> _wires = [];
    private readonly Dictionary<Guid, Point> _positions = [];

    private Guid? _selectedNode;
    private EdgeView? _selectedEdge;
    private CardView? _pressedCard;
    private Point _grabOffset;
    private Point _downPoint;
    private bool _draggingCard;
    private Guid? _wiringFrom;
    private Path? _tempWire;

    public event Action<RouteNode>? EditRequested;
    public event Action<Guid>? DeleteRequested;
    public event Action<Guid, Guid>? EdgeRequested;
    public event Action<RouteEdge>? EdgeDeleted;
    public event Action<Guid, double, double>? NodeMoved;

    public RouteGraphCanvas()
    {
        Focusable = true;
        Background = new SolidColorBrush(Colors.Transparent);

        _emptyHint.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");

        var root = new Grid();
        root.Children.Add(_wireLayer);
        root.Children.Add(_nodeLayer);
        root.Children.Add(_overlayLayer);
        root.Children.Add(_emptyHint);
        Content = root;

        LayoutUpdated += (_, _) => RedrawWires();

        MouseLeftButtonDown += (_, _) =>
        {
            _selectedNode = null;
            _selectedEdge = null;
            RefreshSelectionVisuals();
            Keyboard.Focus(this);
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete) return;

            if (_selectedEdge is { } edge)
            {
                EdgeDeleted?.Invoke(edge.Edge);
                e.Handled = true;
                return;
            }

            if (_selectedNode is { } nodeId)
            {
                DeleteRequested?.Invoke(nodeId);
                e.Handled = true;
            }
        };
    }

    public void SetGraph(IReadOnlyList<RouteNode> nodes, IReadOnlyList<RouteEdge> edges, Guid? activeNodeId = null)
    {
        _nodeLayer.Children.Clear();
        _wireLayer.Children.Clear();
        _overlayLayer.Children.Clear();
        _cards.Clear();
        _wires.Clear();
        _positions.Clear();
        _selectedNode = null;
        _selectedEdge = null;
        _pressedCard = null;
        _wiringFrom = null;
        _tempWire = null;

        foreach (var node in nodes)
        {
            var position = new Point(node.CanvasX, node.CanvasY);
            _positions[node.Id] = position;
            var card = BuildCard(node);
            Canvas.SetLeft(card.Root, position.X);
            Canvas.SetTop(card.Root, position.Y);
            _nodeLayer.Children.Add(card.Root);
            _cards[node.Id] = card;
        }

        foreach (var edge in edges)
        {
            if (!_cards.ContainsKey(edge.FromId) || !_cards.ContainsKey(edge.ToId)) continue;
            _wires.Add(BuildEdge(edge));
        }

        foreach (var wire in _wires)
        {
            _wireLayer.Children.Add(wire.Line);
            _wireLayer.Children.Add(wire.Hit);
        }

        if (activeNodeId is { } id)
        {
            _selectedNode = null;
            ApplyActive(id);
        }

        _emptyHint.Visibility = nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RedrawWires();
    }

    public void SetActive(Guid? nodeId)
    {
        foreach (var card in _cards.Values)
        {
            card.Active = false;
            ApplyCardState(card);
        }

        if (nodeId is { } id)
        {
            ApplyActive(id);
        }
    }

    private void ApplyActive(Guid nodeId)
    {
        if (!_cards.TryGetValue(nodeId, out var card)) return;
        card.Active = true;
        ApplyCardState(card);
    }

    private RouteNode Snapshot(CardView card)
    {
        var node = card.Node;
        var position = _positions[node.Id];
        return new RouteNode(
            node.Kind,
            node.Name,
            position.X,
            position.Y,
            node.AnchorName,
            node.RouteName,
            node.MaxLaps,
            node.MaxDuration,
            node.Id);
    }

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

        var subtitle = new TextBlock
        {
            Text = NodeSubtitle(node),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        subtitle.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var body = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
        body.Children.Add(kindLabel);
        body.Children.Add(nameLabel);
        body.Children.Add(subtitle);

        var inPort = BuildPort(accent);
        inPort.HorizontalAlignment = HorizontalAlignment.Left;
        inPort.Margin = new Thickness(-PortRadius, 0, 0, 0);

        var outPort = BuildPort(accent);
        outPort.HorizontalAlignment = HorizontalAlignment.Right;
        outPort.Margin = new Thickness(0, 0, -PortRadius, 0);

        var layout = new Grid();
        layout.Children.Add(body);
        layout.Children.Add(inPort);
        layout.Children.Add(outPort);

        var root = new Border
        {
            Width = CardWidth,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Child = layout,
        };
        root.SetResourceReference(BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var card = new CardView(node, root);
        ApplyCardState(card);

        root.MouseLeftButtonDown += (_, e) =>
        {
            Keyboard.Focus(this);
            _pressedCard = card;
            _draggingCard = false;
            _grabOffset = e.GetPosition(root);
            _downPoint = e.GetPosition(_nodeLayer);
            root.CaptureMouse();
            e.Handled = true;
        };

        root.MouseMove += (_, e) =>
        {
            if (_pressedCard != card || !root.IsMouseCaptured) return;
            var p = e.GetPosition(_nodeLayer);
            if (!_draggingCard)
            {
                if (Math.Abs(p.X - _downPoint.X) < DragThreshold && Math.Abs(p.Y - _downPoint.Y) < DragThreshold) return;
                _draggingCard = true;
            }

            var x = Math.Max(0, Math.Min(p.X - _grabOffset.X, Math.Max(0, _nodeLayer.ActualWidth - root.ActualWidth)));
            var y = Math.Max(0, Math.Min(p.Y - _grabOffset.Y, Math.Max(0, _nodeLayer.ActualHeight - root.ActualHeight)));
            _positions[node.Id] = new Point(x, y);
            Canvas.SetLeft(root, x);
            Canvas.SetTop(root, y);
            RedrawWires();
        };

        root.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressedCard != card) return;
            root.ReleaseMouseCapture();
            _pressedCard = null;

            if (_draggingCard)
            {
                _draggingCard = false;
                var position = _positions[node.Id];
                NodeMoved?.Invoke(node.Id, position.X, position.Y);
                return;
            }

            if (e.ClickCount >= 2)
            {
                EditRequested?.Invoke(Snapshot(card));
                return;
            }

            _selectedNode = node.Id;
            _selectedEdge = null;
            RefreshSelectionVisuals();
        };

        root.MouseRightButtonDown += (_, e) =>
        {
            DeleteRequested?.Invoke(node.Id);
            e.Handled = true;
        };

        outPort.MouseLeftButtonDown += (_, e) =>
        {
            _wiringFrom = node.Id;
            _tempWire = new Path
            {
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 2,
                StrokeDashArray = [4, 3],
                IsHitTestVisible = false,
            };
            _overlayLayer.Children.Add(_tempWire);
            CaptureMouse();
            UpdateTempWire(e.GetPosition(_nodeLayer));
            e.Handled = true;
        };

        return card;
    }

    private static Ellipse BuildPort(Color accent) => new()
    {
        Width = PortRadius * 2,
        Height = PortRadius * 2,
        VerticalAlignment = VerticalAlignment.Center,
        Fill = new SolidColorBrush(accent),
        Stroke = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
        StrokeThickness = 1,
        Cursor = Cursors.Cross,
    };

    private EdgeView BuildEdge(RouteEdge edge)
    {
        var line = new Path
        {
            StrokeThickness = 2,
            IsHitTestVisible = false,
        };
        line.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");

        var hit = new Path
        {
            Stroke = new SolidColorBrush(Colors.Transparent),
            StrokeThickness = 12,
            Cursor = Cursors.Hand,
        };

        var view = new EdgeView(edge, line, hit);
        hit.MouseLeftButtonDown += (_, e) =>
        {
            _selectedEdge = view;
            _selectedNode = null;
            RefreshSelectionVisuals();
            Keyboard.Focus(this);
            e.Handled = true;
        };
        hit.MouseRightButtonDown += (_, e) =>
        {
            EdgeDeleted?.Invoke(edge);
            e.Handled = true;
        };
        return view;
    }

    private void RefreshSelectionVisuals()
    {
        foreach (var card in _cards.Values)
        {
            card.Selected = card.Node.Id == _selectedNode;
            ApplyCardState(card);
        }

        foreach (var wire in _wires)
        {
            var selected = wire == _selectedEdge;
            wire.Line.StrokeThickness = selected ? 3 : 2;
            if (selected)
            {
                wire.Line.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
            }
            else
            {
                wire.Line.SetResourceReference(Shape.StrokeProperty, "ControlStrongStrokeColorDefaultBrush");
            }
        }
    }

    private void ApplyCardState(CardView card)
    {
        var root = card.Root;
        if (card.Active)
        {
            var accent = KindAccent(card.Node.Kind);
            root.BorderBrush = new SolidColorBrush(accent);
            root.BorderThickness = new Thickness(2.5);
            root.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = accent,
                BlurRadius = 20,
                ShadowDepth = 0,
                Opacity = 0.7,
            };
            return;
        }

        root.Effect = null;
        root.BorderThickness = new Thickness(card.Selected ? 2.5 : 1.5);
        if (card.Selected)
        {
            root.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }
        else
        {
            root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        }
    }

    private Point InPortCenter(Guid nodeId)
    {
        var card = _cards[nodeId];
        var position = _positions[nodeId];
        return new Point(position.X, position.Y + card.Root.ActualHeight / 2);
    }

    private Point OutPortCenter(Guid nodeId)
    {
        var card = _cards[nodeId];
        var position = _positions[nodeId];
        return new Point(position.X + card.Root.ActualWidth, position.Y + card.Root.ActualHeight / 2);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_wiringFrom is not null && _tempWire is not null)
        {
            UpdateTempWire(e.GetPosition(_nodeLayer));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_wiringFrom is null) return;

        ReleaseMouseCapture();
        var from = _wiringFrom.Value;
        _wiringFrom = null;
        _overlayLayer.Children.Remove(_tempWire);
        _tempWire = null;

        var dropPoint = e.GetPosition(_nodeLayer);
        foreach (var (nodeId, _) in _cards)
        {
            if (nodeId == from) continue;
            if ((InPortCenter(nodeId) - dropPoint).Length <= PortHitRadius)
            {
                EdgeRequested?.Invoke(from, nodeId);
                return;
            }
        }
    }

    private void UpdateTempWire(Point mouse)
    {
        if (_wiringFrom is null || _tempWire is null || !_cards.ContainsKey(_wiringFrom.Value)) return;
        _tempWire.Data = WireGeometry(OutPortCenter(_wiringFrom.Value), mouse);
    }

    private void RedrawWires()
    {
        foreach (var wire in _wires)
        {
            if (!_positions.ContainsKey(wire.Edge.FromId) || !_positions.ContainsKey(wire.Edge.ToId)) continue;
            var from = OutPortCenter(wire.Edge.FromId);
            var to = InPortCenter(wire.Edge.ToId);
            if (wire.LastFrom == from && wire.LastTo == to) continue;

            wire.LastFrom = from;
            wire.LastTo = to;
            var geometry = WireGeometry(from, to);
            wire.Line.Data = geometry;
            wire.Hit.Data = geometry;
        }
    }

    private static Geometry WireGeometry(Point from, Point to)
    {
        var bend = Math.Max(40, Math.Abs(to.X - from.X) / 2);
        var figure = new PathFigure { StartPoint = from };
        figure.Segments.Add(new BezierSegment(
            new Point(from.X + bend, from.Y),
            new Point(to.X - bend, to.Y),
            to,
            isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Color KindAccent(RouteNodeKind kind) => kind switch
    {
        RouteNodeKind.Anchor => Color.FromRgb(0x4C, 0x8D, 0xF0),
        RouteNodeKind.Playback => Color.FromRgb(0x57, 0xB0, 0x5C),
        RouteNodeKind.Loop => Color.FromRgb(0xA9, 0x6B, 0xE8),
        _ => Color.FromRgb(0x80, 0x80, 0x80),
    };

    private static string KindLabel(RouteNodeKind kind) => kind switch
    {
        RouteNodeKind.Anchor => "锚点",
        RouteNodeKind.Playback => "回放",
        RouteNodeKind.Loop => "循环",
        _ => "?",
    };

    private static string NodeSubtitle(RouteNode node) => node.Kind switch
    {
        RouteNodeKind.Anchor => node.AnchorName ?? "未选择锚点",
        RouteNodeKind.Playback => node.RouteName ?? "未关联路线",
        RouteNodeKind.Loop => LoopSubtitle(node),
        _ => string.Empty,
    };

    private static string LoopSubtitle(RouteNode node)
    {
        if (node.MaxLaps is null && node.MaxDuration is null) return "无限循环";
        var parts = new List<string>();
        if (node.MaxLaps is { } laps) parts.Add($"{laps} 圈");
        if (node.MaxDuration is { } duration) parts.Add($"{duration.TotalMinutes:0.#} 分钟");
        return string.Join(" / ", parts);
    }

    private sealed class CardView(RouteNode node, Border root)
    {
        public RouteNode Node { get; } = node;
        public Border Root { get; } = root;
        public bool Selected { get; set; }
        public bool Active { get; set; }
    }

    private sealed class EdgeView(RouteEdge edge, Path line, Path hit)
    {
        public RouteEdge Edge { get; } = edge;
        public Path Line { get; } = line;
        public Path Hit { get; } = hit;
        public Point LastFrom { get; set; } = new(double.NaN, double.NaN);
        public Point LastTo { get; set; } = new(double.NaN, double.NaN);
    }
}
