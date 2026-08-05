using System.Windows;
using System.Windows.Controls;
using RocoPilot.Breeding;
using Wpf.Ui.Controls;

namespace RocoPilot.Shell.Pages;

public partial class EggQueryPage : Page
{
    private sealed record OptionChip(string Label, object Value, bool IsActive, bool IsHighlighted, System.Windows.Media.SolidColorBrush? Dot = null);

    private sealed record GroupDot(string Group, System.Windows.Media.SolidColorBrush Fill);

    private sealed record PetCard(Pet Pet)
    {
        public string IndexLabel => $"NO.{Pet.Number:D3}";

        public string Name => Pet.DisplayName;

        public string? ImageUrl => Pet.ImageUrl;

        public IReadOnlyList<GroupDot> Dots => Pet.EggGroups
            .Select(g => new GroupDot(g, EggGroupColors.Of(g)))
            .ToList();

        public System.Windows.Media.LinearGradientBrush Glow
        {
            get
            {
                var brush = new System.Windows.Media.LinearGradientBrush(
                    ParseRgba(Pet.GlowFrom ?? "rgba(119,125,132,0.8)"),
                    ParseRgba(Pet.GlowTo ?? "rgba(119,125,132,0.8)"),
                    new Point(0, 1),
                    new Point(1, 0));
                brush.Freeze();
                return brush;
            }
        }

        private static System.Windows.Media.Color ParseRgba(string value)
        {
            var inner = value[(value.IndexOf('(') + 1)..value.LastIndexOf(')')];
            var parts = inner.Split(',');
            return System.Windows.Media.Color.FromArgb(
                (byte)Math.Round(double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) * 255),
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]));
        }
    }

    private const string AllGroupsValue = "";

    private const int RenderBatch = 120;

    private readonly PetCatalog _catalog;
    private readonly Dictionary<string, Pet> _byDisplayName;
    private readonly List<Pet> _selectedPets = [];
    private readonly List<string> _filterGroups = [];
    private readonly List<PetCard> _resultCards = [];
    private readonly System.Collections.ObjectModel.ObservableCollection<PetCard> _visibleCards = [];
    private int _renderedCount;
    private ScrollViewer? _hostScroll;
    private readonly System.Windows.Threading.DispatcherTimer _hintTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2.5),
    };
    private bool _unionMode = true;
    private string _stageFilter = "stage1";
    private string _shinyFilter = "all";

    public EggQueryPage(PetCatalog catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        _byDisplayName = catalog.Pets.ToDictionary(p => p.DisplayName);
        ResultList.ItemsSource = _visibleCards;
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            SearchHint.Visibility = Visibility.Collapsed;
        };
        Loaded += OnPageLoaded;
        RefreshAll();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_hostScroll is { IsLoaded: true }) return;

        if (_hostScroll is not null)
        {
            _hostScroll.ScrollChanged -= OnHostScrollChanged;
        }

        _hostScroll = FindAncestorScrollViewer(this);
        if (_hostScroll is null)
        {
            while (_renderedCount < _resultCards.Count) RenderMore();
            return;
        }

        _hostScroll.ScrollChanged += OnHostScrollChanged;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var host = _hostScroll;
            if (host is not null && host.ScrollableHeight <= 0)
            {
                while (_renderedCount < _resultCards.Count) RenderMore();
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject child)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer) return scrollViewer;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void OnHostScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var host = _hostScroll;
        if (host is null || host.ScrollableHeight <= 0) return;
        if (host.VerticalOffset >= host.ScrollableHeight - 400) RenderMore();
    }

    private void RenderMore()
    {
        if (_renderedCount >= _resultCards.Count) return;

        var end = Math.Min(_renderedCount + RenderBatch, _resultCards.Count);
        for (var i = _renderedCount; i < end; i++)
        {
            _visibleCards.Add(_resultCards[i]);
        }

        _renderedCount = end;
    }

    private void OnSearchTextChanged(object sender, AutoSuggestBoxTextChangedEventArgs e) =>
        SearchBox.ItemsSource = _catalog.Suggest(SearchBox.Text).Select(p => p.DisplayName).ToList();

    private void OnSuggestionChosen(object sender, AutoSuggestBoxSuggestionChosenEventArgs e)
    {
        if (e.SelectedItem is string displayName)
        {
            AddSelectedPet(_byDisplayName.GetValueOrDefault(displayName) ?? _catalog.Find(displayName), displayName);
        }
    }

    private void OnQuerySubmitted(object sender, AutoSuggestBoxQuerySubmittedEventArgs e) =>
        AddSelectedPet(_catalog.Find(e.QueryText), e.QueryText);

    private void OnRemoveSelectedPetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Pet pet })
        {
            _selectedPets.Remove(pet);
            ApplySelection();
        }
    }

    private void OnResultCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PetCard card })
        {
            AddSelectedPet(card.Pet);
        }
    }

    private void OnGroupChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string group }) return;

        if (group == AllGroupsValue)
        {
            _filterGroups.Clear();
        }
        else if (_filterGroups.Remove(group))
        {
        }
        else
        {
            _filterGroups.Add(group);
            if (_filterGroups.Count > PetCatalog.MaxFilterGroups)
            {
                _filterGroups.RemoveAt(0);
            }
        }

        RefreshAll();
    }

    private void OnModeChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: bool unionMode })
        {
            _unionMode = unionMode;
            RefreshAll();
        }
    }

    private void OnStageChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string stage })
        {
            _stageFilter = stage;
            RefreshAll();
        }
    }

    private void OnShinyChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string shiny })
        {
            _shinyFilter = shiny;
            RefreshAll();
        }
    }

    private void AddSelectedPet(Pet? pet, string query = "")
    {
        if (pet is null)
        {
            if (query.Trim().Length > 0)
            {
                SearchHint.Text = $"未找到「{query.Trim()}」";
                SearchHint.Visibility = Visibility.Visible;
                _hintTimer.Stop();
                _hintTimer.Start();
            }
            return;
        }

        SearchHint.Visibility = Visibility.Collapsed;
        _hintTimer.Stop();

        if (_selectedPets.Contains(pet))
        {
            _selectedPets.Remove(pet);
            ApplySelection();
            return;
        }

        if (_selectedPets.Count >= PetCatalog.MaxFilterGroups)
        {
            _selectedPets.RemoveAt(_selectedPets.Count - 1);
        }

        _selectedPets.Add(pet);
        SearchBox.Text = string.Empty;
        ApplySelection();
    }

    private void ApplySelection()
    {
        _filterGroups.Clear();

        if (_selectedPets.Count == 1)
        {
            _filterGroups.AddRange(_selectedPets[0].EggGroups.Take(PetCatalog.MaxFilterGroups));
        }
        else if (_selectedPets.Count == 2)
        {
            _filterGroups.AddRange(_catalog.SharedBreedGroups(_selectedPets[0], _selectedPets[1]));
        }

        RefreshAll();
    }

    private void RefreshAll()
    {
        SelectedPetChips.ItemsSource = _selectedPets.ToList();
        RefreshChips();
        RefreshResults();
    }

    private void RefreshChips()
    {
        var petGroups = _selectedPets.SelectMany(p => p.EggGroups).ToHashSet();

        GroupChips.ItemsSource = new List<OptionChip>
        {
            new("全部", AllGroupsValue, _filterGroups.Count == 0, false),
        }.Concat(_catalog.Groups.Select(g => new OptionChip(g, g, _filterGroups.Contains(g), petGroups.Contains(g), EggGroupColors.Of(g)))).ToList();

        ModeChips.ItemsSource = new List<OptionChip>
        {
            new("并集", true, _unionMode, false),
            new("交集", false, !_unionMode, false),
        };

        StageChips.ItemsSource = new List<OptionChip>
        {
            new("一阶", "stage1", _stageFilter == "stage1", false),
            new("全部", "all", _stageFilter == "all", false),
        };

        ShinyChips.ItemsSource = new List<OptionChip>
        {
            new("全部", "all", _shinyFilter == "all", false),
            new("普通", "no", _shinyFilter == "no", false),
            new("异色", "yes", _shinyFilter == "yes", false),
        };
    }

    private void RefreshResults()
    {
        IReadOnlyList<string> shared = _selectedPets.Count == 2
            ? _catalog.SharedBreedGroups(_selectedPets[0], _selectedPets[1])
            : [];
        var pairBlocked = _selectedPets.Count == 2 && shared.Count == 0;

        IEnumerable<Pet> query = _catalog.Pets;

        if (pairBlocked)
        {
            query = [];
        }
        else if (_filterGroups.Count > 0)
        {
            query = _unionMode
                ? query.Where(p => _filterGroups.Any(p.EggGroups.Contains))
                : query.Where(p => _filterGroups.All(p.EggGroups.Contains));
        }

        if (_stageFilter == "stage1")
        {
            query = query.Where(p => p.Stage == 1);
        }

        if (_shinyFilter != "all")
        {
            query = query.Where(p => p.Shiny == (_shinyFilter == "yes"));
        }

        var pets = query.ToList();
        ResultSummary.Text = ScopeLabel(shared);
        ResultCount.Text = $"共 {pets.Count} 只";

        _resultCards.Clear();
        _resultCards.AddRange(pets.Select(p => new PetCard(p)));
        _visibleCards.Clear();
        _renderedCount = 0;
        RenderMore();

        EmptyHint.Text = "没有符合筛选的精灵";
        EmptyHint.Visibility = pets.Count == 0 && !pairBlocked ? Visibility.Visible : Visibility.Collapsed;

        RefreshPairVerdict(shared);
    }

    private string ScopeLabel(IReadOnlyList<string> shared)
    {
        if (_selectedPets.Count == 2)
        {
            return shared.Count > 0
                ? $"{_selectedPets[0].Name} 与 {_selectedPets[1].Name} 的共同蛋组"
                : $"{_selectedPets[0].Name} 与 {_selectedPets[1].Name} 配对";
        }

        if (_selectedPets.Count == 1)
        {
            return $"{_selectedPets[0].DisplayName} 的蛋组";
        }

        return _filterGroups.Count switch
        {
            0 => "全部精灵",
            1 => $"{_filterGroups[0]}（该组别所有精灵）",
            _ => string.Join(" + ", _filterGroups) + (_unionMode ? "（并集）" : "（交集）"),
        };
    }

    private void RefreshPairVerdict(IReadOnlyList<string> shared)
    {
        if (_selectedPets.Count == 0)
        {
            PairVerdict.Text = string.Empty;
            return;
        }

        if (_selectedPets.Count == 1)
        {
            var single = _selectedPets[0];
            if (!single.Breedable)
            {
                PairVerdict.Text = $"{single.Name} 无法生蛋，不能与任何精灵配对";
                PairVerdict.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorAttentionBrush");
            }
            else
            {
                PairVerdict.Text = "再选一只精灵即可查询配对";
                PairVerdict.Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush");
            }
            return;
        }

        var left = _selectedPets[0];
        var right = _selectedPets[1];
        var sterile = !left.Breedable ? left : !right.Breedable ? right : null;

        if (sterile is not null)
        {
            PairVerdict.Text = $"{sterile.Name} 无法生蛋，不能与任何精灵配对";
        }
        else if (shared.Count > 0)
        {
            PairVerdict.Text = $"可一起孵蛋 · 共同蛋组：{string.Join("、", shared)}";
        }
        else
        {
            PairVerdict.Text = $"{left.Name} 与 {right.Name} 没有共同蛋组，无法一起孵蛋";
        }

        PairVerdict.Foreground = sterile is not null || shared.Count == 0
            ? (System.Windows.Media.Brush)FindResource("SystemFillColorAttentionBrush")
            : (System.Windows.Media.Brush)FindResource("SystemFillColorSuccessBrush");
    }

    private void OnWikiLinkNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = e.Uri.ToString(),
            UseShellExecute = true,
        });
        e.Handled = true;
    }
}
