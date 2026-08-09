using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Routing;
using RocoPilot.Scripting;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Services;
using UiButton = Wpf.Ui.Controls.Button;
using UiToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Page = System.Windows.Controls.Page;
using TextBlock = System.Windows.Controls.TextBlock;

namespace RocoPilot.Shell.Pages;

public partial class RoutePage : Page
{
    private const string DefaultGraphName = "新流程";

    // 公交式竖向站点：左列宽度 = 圆点直径 + 2 * 圆点水平边距，竖线在圆心
    private const double RailColumnWidth = 44;
    private const double StationDotSize = 22;
    private const double RailThickness = 2;

    private static readonly SolidColorBrush AccentBrush = RouteVisuals.AccentBrush;
    private static readonly SolidColorBrush StartBrush = RouteVisuals.StartBrush;
    private static readonly SolidColorBrush EndBrush = RouteVisuals.EndBrush;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly RouteStore _store;
    private readonly ScriptStore _scriptStore;
    private readonly CaptureHost _capture;
    private readonly DispatcherHost _dispatcher;

    private readonly HookStrokeRecorder _recorder = new();
    private bool _recording;
    private RecordedScript? _pendingRecord;
    private UiButton? _recordButton;
    private Grid? _saveRow;
    private TextBox? _nameBox;

    private readonly StackPanel _timeline = new();
    private readonly Dictionary<Guid, FrameworkElement> _stepCards = new();

    private string _name = DefaultGraphName;
    private List<ActionNode> _nodes = [];
    private bool _loopsToHead;
    private int? _maxLaps;
    private TimeSpan? _maxDuration;

    private bool _loaded;
    private bool _suppressEvents;
    private Guid? _expandedId;
    private Guid? _activeId;
    private int? _pickerInsertIndex;
    private DispatcherTimer? _undoTimer;
    private Guid? _dragNodeId;
    private Point _dragStart;

    public RoutePage(RouteStore store, ScriptStore scriptStore, CaptureHost capture, DispatcherHost dispatcher)
    {
        InitializeComponent();
        _store = store;
        _scriptStore = scriptStore;
        _capture = capture;
        _dispatcher = dispatcher;

        TimelineHost.Children.Add(_timeline);
        Loaded += OnLoaded;

        _dispatcher.EventRaised += OnDispatcherEvent;
        _dispatcher.Changed += OnDispatcherChanged;

        Unloaded += (_, _) =>
        {
            if (_recording)
            {
                try { _recorder.Stop("未命名"); } catch { }
                _recording = false;
            }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            UpdateRunPill();
            return;
        }
        _loaded = true;

        try
        {
            var graph = await _store.LoadGraphAsync();
            _name = graph.Name;
            _nodes = [.. graph.Nodes];
            _loopsToHead = graph.LoopsToHead;
            _maxLaps = graph.MaxLaps;
            _maxDuration = graph.MaxDuration;
        }
        catch (FileNotFoundException)
        {
        }
        catch (InvalidDataException)
        {
            // 执行图损坏/无法解析时，降级为新建流程，避免页面崩溃
        }

        Rebuild();
    }

    private void Rebuild()
    {
        _timeline.Children.Clear();
        _stepCards.Clear();

        _timeline.Children.Add(BuildHero());

        for (var i = 0; i < _nodes.Count; i++)
        {
            _timeline.Children.Add(StationRow(_nodes[i], i));
        }

        if (_pickerInsertIndex is { } insertIndex)
        {
            _timeline.Children.Add(PickerRow(insertIndex));
        }

        _timeline.Children.Add(BuildAddRow());
        _timeline.Children.Add(BuildLoopSection());

        if (_nodes.Count == 0 && _pickerInsertIndex is null)
        {
            var hint = new TextBlock
            {
                Text = "添加后，拖拽站点可调整顺序",
                FontSize = 12,
                Margin = new Thickness(44, 6, 0, 0),
            };
            hint.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
            _timeline.Children.Add(hint);
        }

        ApplyActiveStroke();
        UpdateRunPill();
    }

    private FrameworkElement BuildHero()
    {
        // 工具箱里的一个功能页：不放大标题，仅一行小号流程名（可重命名）+ 运行按钮
        var nameText = new TextBlock
        {
            Text = _name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.IBeam,
            ToolTip = "点击重命名",
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameText.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        nameText.MouseLeftButtonUp += (_, _) => BeginRenameRow(nameText);

        var record = new UiButton
        {
            Content = "录制",
            FontSize = 12,
            Padding = new Thickness(12, 5, 12, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
        };
        record.Click += (_, _) => OnRecordToggle();
        if (_recording)
        {
            record.Content = "● 停止";
            record.Appearance = Wpf.Ui.Controls.ControlAppearance.Danger;
        }
        _recordButton = record;

        var run = new UiButton
        {
            Content = "运行",
            FontSize = 12,
            Padding = new Thickness(14, 5, 14, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
        };
        run.Click += (_, _) => RunRoute(null, singleNode: false);
        run.Tag = "run-pill";

        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nameText, 0);
        Grid.SetColumn(record, 2);
        Grid.SetColumn(run, 3);
        toolbar.Children.Add(nameText);
        toolbar.Children.Add(record);
        toolbar.Children.Add(run);

        var nameBox = new TextBox
        {
            Width = 240,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameBox.SetResourceReference(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        _nameBox = nameBox;

        var save = new UiButton
        {
            Content = "保存",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(8, 0, 0, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
        };
        save.Click += async (_, _) => await SaveRecordAsync();

        var discard = new UiButton
        {
            Content = "放弃",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(8, 0, 0, 0),
        };
        discard.Click += (_, _) => DiscardRecord();

        var saveRow = new Grid
        {
            Margin = new Thickness(0, 10, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nameBox, 0);
        Grid.SetColumn(save, 1);
        Grid.SetColumn(discard, 2);
        saveRow.Children.Add(nameBox);
        saveRow.Children.Add(save);
        saveRow.Children.Add(discard);
        _saveRow = saveRow;

        var hero = new StackPanel { Orientation = Orientation.Vertical };
        hero.Children.Add(toolbar);
        hero.Children.Add(saveRow);
        var heroCard = CardShell(hero);
        heroCard.Margin = new Thickness(RailColumnWidth, 0, 0, 14);
        return heroCard;
    }

    private void OnRecordToggle()
    {
        if (_recording) StopRecording();
        else StartRecording();
    }

    private void StartRecording()
    {
        try
        {
            _recorder.Start(BuildGameFocusedCheck());
            _recording = true;
            HideSaveRow();
            _recordButton!.Content = "● 停止";
            _recordButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Danger;
        }
        catch (Exception ex)
        {
            _recording = false;
            ShowToast($"录制启动失败：{ex.Message}");
        }
    }

    private void StopRecording()
    {
        try
        {
            _pendingRecord = _recorder.Stop("未录制");
            _recording = false;
            RestoreRecordButton();
            _nameBox!.Text = "未命名";
            _saveRow!.Visibility = Visibility.Visible;
            _nameBox.Focus();
            _nameBox.SelectAll();
        }
        catch (Exception ex)
        {
            _recording = false;
            RestoreRecordButton();
            ShowToast($"停止录制失败：{ex.Message}");
        }
    }

    private async Task SaveRecordAsync()
    {
        if (_pendingRecord is null) return;

        var name = _nameBox!.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _nameBox.Focus();
            return;
        }

        try
        {
            var script = new RecordedScript(name, _pendingRecord.Strokes, _pendingRecord.CreatedAt);
            await _scriptStore.SaveAsync(script);
            _pendingRecord = null;
            HideSaveRow();
            ShowToast($"已保存「{script.Name}」，可在下方添加「脚本回放」步骤。");
            Rebuild();
        }
        catch (Exception ex)
        {
            ShowToast($"保存失败：{ex.Message}");
        }
    }

    private void DiscardRecord()
    {
        _pendingRecord = null;
        HideSaveRow();
        ShowToast("已放弃本次录制。");
    }

    private void HideSaveRow()
    {
        if (_saveRow is not null) _saveRow.Visibility = Visibility.Collapsed;
    }

    private void RestoreRecordButton()
    {
        if (_recordButton is null) return;
        _recordButton.Content = "录制";
        _recordButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    private Func<bool> BuildGameFocusedCheck()
    {
        var gameProcessId = WindowFinder.FindProcessId(WindowFinder.GameProcessName);
        return () => WindowFinder.IsForegroundProcess(gameProcessId);
    }

    private void BeginRenameRow(TextBlock nameText)
    {
        if (!EnsureEditable()) return;

        if (nameText.Parent is not Grid grid) return;

        var box = new TextBox
        {
            Text = _name == DefaultGraphName ? string.Empty : _name,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");

        grid.Children.Remove(nameText);
        grid.Children.Insert(0, box);
        box.Focus();
        box.SelectAll();

        var commit = () =>
        {
            var next = box.Text.Trim();
            _name = next.Length > 0 ? next : DefaultGraphName;
            grid.Children.Remove(box);
            grid.Children.Insert(0, nameText);
            nameText.Text = _name;
            SaveAndRebuild();
        };

        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) commit();
            else if (args.Key == Key.Escape)
            {
                grid.Children.Remove(box);
                grid.Children.Insert(0, nameText);
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (grid.Children.Contains(box)) commit();
        };
    }

    private FrameworkElement StationRow(ActionNode node, int index)
    {
        var isFirst = index == 0;
        var isLast = index == _nodes.Count - 1;
        var accent = isFirst ? StartBrush : isLast ? EndBrush : AccentBrush;

        const double DotSize = 12;
        const double DotCenterY = 26;

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = DotSize,
            Height = DotSize,
            Fill = accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, DotCenterY - DotSize / 2.0, 0, 0),
            ToolTip = isFirst ? "起点" : isLast ? "终点" : "锚点可用",
        };

        var railTop = new System.Windows.Shapes.Rectangle
        {
            Width = RailThickness,
            Height = DotCenterY,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            RadiusX = 1,
            RadiusY = 1,
            IsHitTestVisible = false,
            Visibility = isFirst ? Visibility.Collapsed : Visibility.Visible,
        };
        railTop.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ControlStrokeColorDefaultBrush");

        var railBottom = new System.Windows.Shapes.Rectangle
        {
            Width = RailThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, DotCenterY, 0, 0),
            RadiusX = 1,
            RadiusY = 1,
            IsHitTestVisible = false,
            Visibility = isLast ? Visibility.Collapsed : Visibility.Visible,
        };
        railBottom.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ControlStrokeColorDefaultBrush");

        var axis = new Grid { Width = RailColumnWidth };
        axis.Children.Add(railTop);
        axis.Children.Add(railBottom);
        axis.Children.Add(dot);

        var title = new TextBlock
        {
            Text = node.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var sub = new TextBlock
        {
            Text = isFirst ? "起点 · 从这里出发"
                 : isLast ? "终点 · 流程在这里收尾"
                 : NodeSubtitle(node),
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
        };
        sub.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(title);
        textStack.Children.Add(sub);

        var badge = new TextBlock
        {
            Text = isFirst ? "起点" : isLast ? "终点" : string.Empty,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isFirst || isLast ? Visibility.Visible : Visibility.Collapsed,
        };
        if (isFirst)
        {
            badge.Foreground = RouteVisuals.StartBrush;
            badge.Background = RouteVisuals.StartSoftBrush;
        }
        else if (isLast)
        {
            badge.Foreground = RouteVisuals.EndBrush;
            badge.Background = RouteVisuals.EndSoftBrush;
        }

        var chevron = ChevronGlyph();

        var front = new Grid();
        front.ColumnDefinitions.Add(new ColumnDefinition());
        front.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        front.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(textStack, 0);
        Grid.SetColumn(badge, 1);
        Grid.SetColumn(chevron, 2);
        front.Children.Add(textStack);
        front.Children.Add(badge);
        front.Children.Add(chevron);

        var cardContent = new StackPanel();
        cardContent.Children.Add(front);

        var card = CardShell(cardContent);
        _stepCards[node.Id] = card;

        if (_expandedId == node.Id)
        {
            cardContent.Children.Add(BuildStepExpander(node));
        }

        card.MouseLeftButtonUp += (_, _) => ToggleExpanded(node.Id);
        card.ContextMenu = StepContextMenu(node);
        card.AllowDrop = true;
        card.DragOver += (_, args) =>
        {
            args.Effects = _dragNodeId is null ? DragDropEffects.None : DragDropEffects.Move;
            args.Handled = true;
        };
        card.Drop += (_, args) => OnDropOnCard(node.Id, args);

        card.MouseMove += (_, args) =>
        {
            if (_dragNodeId != node.Id || args.LeftButton != MouseButtonState.Pressed) return;
            if (Distance(args.GetPosition(null), _dragStart) < 4) return;
            DragDrop.DoDragDrop(card, node.Id.ToString(), DragDropEffects.Move);
            _dragNodeId = null;
        };
        card.MouseLeftButtonDown += (_, args) =>
        {
            _dragNodeId = node.Id;
            _dragStart = args.GetPosition(null);
        };
        card.MouseLeftButtonUp += (_, _) => _dragNodeId = null;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(axis, 0);
        Grid.SetColumn(card, 1);
        row.Children.Add(axis);
        row.Children.Add(card);
        return row;
    }

    private void OnDropOnCard(Guid targetId, DragEventArgs args)
    {
        if (_dragNodeId is not { } sourceId || sourceId == targetId) return;
        if (!EnsureEditable()) return;

        var from = _nodes.FindIndex(n => n.Id == sourceId);
        var to = _nodes.FindIndex(n => n.Id == targetId);
        if (from < 0 || to < 0) return;

        var node = _nodes[from];
        _nodes.RemoveAt(from);
        _nodes.Insert(to, node);
        SaveAndRebuild();
    }

    private ContextMenu StepContextMenu(ActionNode node)
    {
        var menu = new ContextMenu();

        var insert = new MenuItem { Header = "在其后插入" };
        insert.Click += (_, _) =>
        {
            if (!EnsureEditable()) return;
            _pickerInsertIndex = _nodes.FindIndex(n => n.Id == node.Id) + 1;
            _expandedId = null;
            Rebuild();
        };

        var remove = new MenuItem { Header = "删除" };
        remove.Click += (_, _) => DeleteNode(node.Id);

        menu.Items.Add(insert);
        menu.Items.Add(remove);
        return menu;
    }

    private FrameworkElement BuildStepExpander(ActionNode node)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 2),
        };

        switch (node.Kind)
        {
            case ActionKind.Teleport:
                BuildTeleportEditor(panel, (TeleportNode)node);
                break;
            case ActionKind.Delay:
                BuildDelayEditor(panel, (DelayNode)node);
                break;
            case ActionKind.ScriptReplay:
                BuildScriptEditor(panel, (ScriptReplayNode)node);
                break;
        }

        var trial = new UiButton
        {
            Content = "试运行此步",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        trial.Click += (_, _) => RunRoute(node.Id, singleNode: true);
        panel.Children.Add(trial);
        return panel;
    }

    private void BuildTeleportEditor(StackPanel panel, TeleportNode node)
    {
        panel.Children.Add(ExpanderLabel("传送锚点"));

        var combo = new ComboBox
        {
            IsEditable = true,
            Text = node.AnchorName,
            Margin = new Thickness(0, 6, 0, 0),
        };
        foreach (var entry in AnchorCatalog.GroundEntries) combo.Items.Add(entry.Name);
        combo.SelectionChanged += (_, _) => ApplyAnchor(node, combo);
        panel.Children.Add(combo);
    }

    private void BuildDelayEditor(StackPanel panel, DelayNode node)
    {
        panel.Children.Add(ExpanderLabel("延时·秒"));

        var box = new TextBox
        {
            Text = node.Duration.TotalSeconds.ToString("0.#"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        box.LostFocus += (_, _) => ApplyDelay(node, box);
        panel.Children.Add(box);
    }

    private void BuildScriptEditor(StackPanel panel, ScriptReplayNode node)
    {
        panel.Children.Add(ExpanderLabel("回放脚本"));

        var combo = new ComboBox
        {
            IsEditable = true,
            Text = node.ScriptName,
            Margin = new Thickness(0, 6, 0, 0),
        };
        foreach (var summary in _scriptStore.List()) combo.Items.Add(summary.Name);
        combo.SelectionChanged += (_, _) => ApplyScript(node, combo);
        panel.Children.Add(combo);
    }

    private void ApplyScript(ScriptReplayNode node, ComboBox combo)
    {
        var name = (combo.SelectedItem as string) ?? combo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == node.ScriptName) return;
        if (_scriptStore.List().All(summary => summary.Name != name))
        {
            combo.Text = node.ScriptName;
            return;
        }
        if (!EnsureEditable()) return;

        ReplaceNode(new ScriptReplayNode($"回放·{name}", name, node.Id));
    }

    private void ApplyAnchor(TeleportNode node, ComboBox combo)
    {
        var name = (combo.SelectedItem as string) ?? combo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name == node.AnchorName) return;
        if (AnchorCatalog.GroundEntries.All(entry => entry.Name != name))
        {
            combo.Text = node.AnchorName;
            return;
        }
        if (!EnsureEditable()) return;

        ReplaceNode(new TeleportNode($"传送·{name}", name, node.Id));
    }

    private void ApplyDelay(DelayNode node, TextBox box)
    {
        if (!EnsureEditable()) return;

        if (!double.TryParse(box.Text.Trim(), out var seconds) || seconds <= 0)
        {
            box.Text = node.Duration.TotalSeconds.ToString("0.#");
            return;
        }

        ReplaceNode(new DelayNode($"延时 {seconds:0.#} 秒", TimeSpan.FromSeconds(seconds), node.Id));
    }

    private FrameworkElement BuildAddRow()
    {
        var text = new TextBlock
        {
            Text = "添加步骤",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
        };
        text.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var plus = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Add24,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Width = StationDotSize,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        plus.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(plus, 0);
        Grid.SetColumn(text, 1);
        row.Children.Add(plus);
        row.Children.Add(text);

        row.MouseLeftButtonUp += (_, _) =>
        {
            if (!EnsureEditable()) return;
            _pickerInsertIndex = _nodes.Count;
            Rebuild();
        };
        row.Cursor = Cursors.Hand;
        row.Margin = new Thickness(0, 0, 0, 14);
        return row;
    }

    private FrameworkElement PickerRow(int insertIndex)
    {
        var kindCombo = new ComboBox
        {
            FontSize = 13,
        };
        kindCombo.Items.Add("传送步骤");
        kindCombo.Items.Add("延时");
        kindCombo.Items.Add("脚本回放");
        kindCombo.SelectedIndex = 0;

        var search = new TextBox { FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
        search.SetResourceReference(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");

        var delayBox = new TextBox
        {
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        delayBox.SetResourceReference(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        delayBox.Text = "5";

        var list = new ListBox
        {
            Height = 180,
            Margin = new Thickness(0, 8, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };

        var scriptList = new ListBox
        {
            Height = 180,
            Margin = new Thickness(0, 8, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        foreach (var summary in _scriptStore.List()) scriptList.Items.Add(summary.Name);

        void RefreshFields()
        {
            var kind = kindCombo.SelectedIndex;
            var isDelay = kind == 1;
            var isScript = kind == 2;
            search.Visibility = isDelay || isScript ? Visibility.Collapsed : Visibility.Visible;
            list.Visibility = isDelay || isScript ? Visibility.Collapsed : Visibility.Visible;
            delayBox.Visibility = isDelay ? Visibility.Visible : Visibility.Collapsed;
            scriptList.Visibility = isScript ? Visibility.Visible : Visibility.Collapsed;
        }
        kindCombo.SelectionChanged += (_, _) => RefreshFields();

        void Populate(string? filter)
        {
            list.Items.Clear();
            foreach (var entry in AnchorCatalog.GroundEntries)
            {
                if (string.IsNullOrWhiteSpace(filter)
                    || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    list.Items.Add(entry.Name);
                }
            }
        }
        Populate(null);
        search.TextChanged += (_, _) => Populate(search.Text.Trim());

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string anchor) return;
            if (!EnsureEditable()) return;

            var node = new TeleportNode($"传送·{anchor}", anchor);
            InsertNode(node, insertIndex);
        };

        scriptList.SelectionChanged += (_, _) =>
        {
            if (scriptList.SelectedItem is not string scriptName) return;
            if (!EnsureEditable()) return;

            var node = new ScriptReplayNode($"回放·{scriptName}", scriptName);
            InsertNode(node, insertIndex);
        };

        delayBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            if (!EnsureEditable()) return;

            if (!double.TryParse(delayBox.Text.Trim(), out var seconds) || seconds <= 0)
                return;

            var node = new DelayNode($"延时 {seconds:0.#} 秒", TimeSpan.FromSeconds(seconds));
            InsertNode(node, insertIndex);
        };

        void InsertNode(ActionNode node, int index)
        {
            _nodes.Insert(Math.Clamp(index, 0, _nodes.Count), node);
            _pickerInsertIndex = null;
            _expandedId = node.Id;
            SaveAndRebuild();
        }

        var panel = new StackPanel();
        panel.Children.Add(kindCombo);
        panel.Children.Add(search);
        panel.Children.Add(delayBox);
        panel.Children.Add(scriptList);
        panel.Children.Add(list);

        var card = CardShell(panel);
        card.Loaded += (_, _) => search.Focus();

        var rail = new System.Windows.Shapes.Rectangle
        {
            Width = RailThickness,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            RadiusX = 1,
            RadiusY = 1,
            IsHitTestVisible = false,
        };
        rail.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ControlStrokeColorDefaultBrush");
        var axis = new Grid { Width = RailColumnWidth };
        axis.Children.Add(rail);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(axis, 0);
        Grid.SetColumn(card, 1);
        row.Children.Add(axis);
        row.Children.Add(card);
        return row;
    }

    private FrameworkElement BuildLoopSection()
    {
        var icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowCounterclockwise24,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        icon.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        var axis = new Grid { Width = RailColumnWidth };
        axis.Children.Add(icon);

        var summary = new TextBlock
        {
            Text = LoopSummary(),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        summary.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");

        var toggle = new UiToggleSwitch { VerticalAlignment = VerticalAlignment.Center };
        _suppressEvents = true;
        toggle.IsChecked = _loopsToHead;
        _suppressEvents = false;
        toggle.Checked += (_, _) => SetLoop(true, summary);
        toggle.Unchecked += (_, _) => SetLoop(false, summary);

        var front = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        front.ColumnDefinitions.Add(new ColumnDefinition());
        front.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(summary, 0);
        Grid.SetColumn(toggle, 1);
        front.Children.Add(summary);
        front.Children.Add(toggle);

        var cardContent = new StackPanel();
        cardContent.Children.Add(front);

        if (_endExpanded)
        {
            cardContent.Children.Add(BuildLoopExpander(summary));
        }

        var card = CardShell(cardContent);
        card.MouseLeftButtonUp += (_, args) =>
        {
            if (IsInsideToggleSwitch(args.OriginalSource as DependencyObject)) return;
            if (!EnsureEditable()) return;
            _endExpanded = !_endExpanded;
            SaveAndRebuild();
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailColumnWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(axis, 0);
        Grid.SetColumn(card, 1);
        row.Children.Add(axis);
        row.Children.Add(card);
        return row;
    }

    private bool _endExpanded;

    private FrameworkElement BuildLoopExpander(TextBlock summary)
    {
        var lapsBox = new TextBox
        {
            Text = _maxLaps?.ToString() ?? string.Empty,
            Margin = new Thickness(0, 6, 0, 0),
        };
        lapsBox.LostFocus += (_, _) => ApplyLoopLimits(lapsBox, minutesBox: null, summary);

        var minutesBox = new TextBox
        {
            Text = _maxDuration is { } d ? d.TotalMinutes.ToString("0.#") : string.Empty,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 2) };
        panel.Children.Add(ExpanderLabel("圈数上限（留空不限）"));
        panel.Children.Add(lapsBox);
        panel.Children.Add(ExpanderLabel("时长上限·分钟（留空不限）"));
        panel.Children.Add(minutesBox);

        minutesBox.LostFocus += (_, _) => ApplyLoopLimits(null, minutesBox, summary);
        return panel;
    }

    private void ApplyLoopLimits(TextBox? lapsBox, TextBox? minutesBox, TextBlock summary)
    {
        if (lapsBox is not null && lapsBox.Text.Trim().Length > 0
            && (!int.TryParse(lapsBox.Text.Trim(), out var laps) || laps < 1))
        {
            lapsBox.Text = _maxLaps?.ToString() ?? string.Empty;
            return;
        }

        if (minutesBox is not null && minutesBox.Text.Trim().Length > 0
            && (!double.TryParse(minutesBox.Text.Trim(), out var minutes) || minutes <= 0))
        {
            minutesBox.Text = _maxDuration is { } d ? d.TotalMinutes.ToString("0.#") : string.Empty;
            return;
        }

        if (lapsBox is not null)
            _maxLaps = int.TryParse(lapsBox.Text.Trim(), out var parsedLaps) ? parsedLaps : null;
        if (minutesBox is not null)
            _maxDuration = double.TryParse(minutesBox.Text.Trim(), out var parsedMinutes)
                ? TimeSpan.FromMinutes(parsedMinutes)
                : null;

        summary.Text = LoopSummary();
        SaveAndRebuild();
    }

    private void SetLoop(bool enabled, TextBlock summary)
    {
        if (_suppressEvents) return;
        if (!EnsureEditable()) return;

        _loopsToHead = enabled;
        if (enabled && _maxLaps is null && _maxDuration is null) _maxLaps = 3;
        summary.Text = LoopSummary();
        SaveAndRebuild();
    }

    private void ToggleExpanded(Guid nodeId)
    {
        if (!EnsureEditable()) return;
        _expandedId = _expandedId == nodeId ? null : nodeId;
        _endExpanded = false;
        Rebuild();
    }

    private void DeleteNode(Guid nodeId)
    {
        if (!EnsureEditable()) return;

        var index = _nodes.FindIndex(n => n.Id == nodeId);
        if (index < 0) return;

        var node = _nodes[index];
        _nodes.RemoveAt(index);
        if (_expandedId == nodeId) _expandedId = null;
        SaveAndRebuild();
        ShowToast($"已删除「{node.Name}」");
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;

        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _undoTimer.Tick += (_, _) => HideToast();
        _undoTimer.Start();
    }

    private void HideToast()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        Toast.Visibility = Visibility.Collapsed;
    }

    private void RunRoute(Guid? startNodeId, bool singleNode)
    {
        if (_dispatcher.RouteExecutionEnabled)
        {
            _dispatcher.RouteExecutionEnabled = false;
            _dispatcher.SyncEnables();
            UpdateRunPill();
            return;
        }

        if (_nodes.Count == 0 || !_capture.IsRunning) return;

        _ = SaveGraphAsync();
        _dispatcher.StartRouteExecution(startNodeId, singleNode);
        WindowFinder.ActivateGameWindow();
        UpdateRunPill();
    }

    private void OnDispatcherChanged() => Dispatcher.BeginInvoke(UpdateRunPill);

    private void OnDispatcherEvent(object? sender, ToolEvent toolEvent) => Dispatcher.BeginInvoke(() =>
    {
        switch (toolEvent.Name)
        {
            case "node_started":
                if (toolEvent.Data?["node_id"] is string nodeIdText
                    && Guid.TryParse(nodeIdText, out var nodeId))
                {
                    _activeId = nodeId;
                    ApplyActiveStroke();
                }
                break;

            case "execution_faulted":
                if (toolEvent.Data?["reason"] is string replayReason)
                    ShowToast(replayReason);
                break;

            case "route_suspended":
            case "graph_finished":
            case "route_fault":
                _dispatcher.RouteExecutionEnabled = false;
                _dispatcher.SyncEnables();
                _activeId = null;
                ApplyActiveStroke();
                UpdateRunPill();
                break;
        }
    });

    private void ApplyActiveStroke()
    {
        foreach (var (nodeId, element) in _stepCards)
        {
            if (nodeId == _activeId)
            {
                if (element is System.Windows.Controls.Border border)
                {
                    border.BorderBrush = RouteVisuals.AccentBrush;
                    border.BorderThickness = new Thickness(1.5);
                }
            }
            else if (element is System.Windows.Controls.Border idle)
            {
                idle.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                idle.BorderThickness = new Thickness(1);
            }
        }
    }

    private void UpdateRunPill()
    {
        if (TimelineHost is null) return;
        var pill = FindRunPill(_timeline);
        if (pill is null) return;

        var running = _dispatcher.RouteExecutionEnabled;
        pill.Content = running ? "停止" : "运行";
        pill.Appearance = running
            ? Wpf.Ui.Controls.ControlAppearance.Danger
            : Wpf.Ui.Controls.ControlAppearance.Primary;
    }

    private static UiButton? FindRunPill(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UiButton { Tag: "run-pill" } pill) return pill;
            if (FindRunPill(child) is { } found) return found;
        }

        return null;
    }

    private string LoopSummary()
    {
        if (!_loopsToHead) return "循环：关闭";

        var parts = new List<string>();
        if (_maxLaps is { } laps) parts.Add($"{laps} 圈");
        if (_maxDuration is { } duration) parts.Add($"上限 {duration.TotalMinutes:0.#} 分钟");
        return parts.Count == 0 ? "循环 · 不限" : $"循环 {string.Join(" · ", parts)}";
    }

    private void ReplaceNode(ActionNode replacement)
    {
        var index = _nodes.FindIndex(n => n.Id == replacement.Id);
        if (index < 0) return;
        _nodes[index] = replacement;
        SaveAndRebuild();
    }

    private RouteGraph BuildGraph() => new(_name, _nodes, _loopsToHead, _maxLaps, _maxDuration);

    private void SaveAndRebuild()
    {
        Rebuild();
        _ = SaveGraphAsync();
    }

    private async Task SaveGraphAsync()
    {
        try
        {
            await _store.SaveGraphAsync(BuildGraph());
        }
        catch (Exception)
        {
        }
    }

    private bool EnsureEditable() => !_dispatcher.RouteExecutionEnabled;

    private static TextBlock ExpanderLabel(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12 };
        label.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        return label;
    }

    private static string NodeSubtitle(ActionNode node) => node.Kind switch
    {
        ActionKind.Teleport => $"传送 · {((TeleportNode)node).AnchorName}",
        ActionKind.Delay => $"延时 · {((DelayNode)node).Duration.TotalSeconds:0.#} 秒",
        ActionKind.ScriptReplay => $"脚本 · {((ScriptReplayNode)node).ScriptName}",
        _ => string.Empty,
    };

    private static Wpf.Ui.Controls.SymbolIcon ChevronGlyph()
    {
        var chevron = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronRight24,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
        return chevron;
    }

    private static System.Windows.Controls.Border CardShell(FrameworkElement content) =>
        new()
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            BorderThickness = new Thickness(1),
            Background = Res("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = Res("ControlStrokeColorDefaultBrush"),
            Child = content,
        };

    private static bool IsInsideToggleSwitch(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is UiToggleSwitch) return true;
            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static Brush Res(string key) =>
        Application.Current.TryFindResource(key) as Brush
        ?? throw new InvalidOperationException($"missing theme resource {key}");
}