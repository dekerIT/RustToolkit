using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using RustToolkit.Desktop.Models;
using RustToolkit.Desktop.Services;

namespace RustToolkit.Desktop.Views;

public partial class CraftingPage : System.Windows.Controls.UserControl
{
    private const int MaximumSearchResults = 24;
    private const long MaximumRequestedAmount = 1_000_000;
    private const int MaximumRecipeDepth = 24;

    private readonly RustHelpItemService _apiService;
    private readonly AppPreferencesStore _preferencesStore;
    private readonly AppPreferences _preferences;
    private readonly ObservableCollection<RustHelpItemChoice> _searchResults = [];
    private readonly ObservableCollection<RustHelpItemChoice> _favoriteResults = [];
    private readonly ObservableCollection<CraftingStepRow> _craftSteps = [];
    private readonly ObservableCollection<CraftingMaterialRow> _materials = [];
    private Dictionary<string, RustHelpItemSummary>? _itemsByPageSlug;
    private RustHelpItemSummary? _selectedItem;
    private RustHelpItemDetailsResult? _selectedDetails;
    private CancellationTokenSource? _detailsLoad;
    private CancellationTokenSource? _calculationLoad;
    private bool _initialized;

    public CraftingPage(
        RustHelpItemService apiService,
        AppPreferencesStore preferencesStore,
        AppPreferences preferences)
    {
        InitializeComponent();
        _apiService = apiService;
        _preferencesStore = preferencesStore;
        _preferences = preferences;
        ItemList.ItemsSource = _searchResults;
        FavoriteList.ItemsSource = _favoriteResults;
        CraftStepsList.ItemsSource = _craftSteps;
        MaterialsList.ItemsSource = _materials;
    }

    private async void CraftingPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            _catalog = await _apiService.LoadCachedCatalogAsync();
            if (_catalog is not null)
            {
                ApplyCatalog(_catalog);
                DataStatusText.Text = FormatSourceStatus(_catalog, "Saved RustHelp catalog");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            ShowError($"Could not read the saved RustHelp item catalog: {exception.Message}");
        }

        await RefreshCatalogAsync();
    }

    private RustHelpCatalog? _catalog;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshCatalogAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateSearchResults();

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: RustHelpItemChoice choice })
        {
            return;
        }

        choice.IsFavorite = !choice.IsFavorite;
        var favoriteKey = GetFavoriteKey(choice.Item);
        if (choice.IsFavorite)
        {
            _preferences.FavoriteCraftingShortNames.Add(favoriteKey);
        }
        else
        {
            _preferences.FavoriteCraftingShortNames.RemoveAll(shortName =>
                string.Equals(shortName, favoriteKey, StringComparison.OrdinalIgnoreCase));
        }

        UpdateSearchResults();
        try
        {
            _preferencesStore.Save(_preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowError($"Favorite changed for this session but could not be saved: {exception.Message}");
        }

        e.Handled = true;
    }

    private async void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox
            || listBox.SelectedItem is not RustHelpItemChoice choice)
        {
            return;
        }

        if (ReferenceEquals(listBox, ItemList))
        {
            FavoriteList.SelectedIndex = -1;
        }
        else
        {
            ItemList.SelectedIndex = -1;
        }

        _selectedItem = choice.Item;
        _calculationLoad?.Cancel();
        _selectedDetails = null;
        CalculateButton.IsEnabled = false;
        SelectedItemName.Text = choice.Item.DisplayName;
        SelectedItemGlyph.Text = choice.Initials;
        SelectedItemImage.Source = null;
        SelectedItemDetails.Text = $"{choice.Item.ShortName} · ID {choice.Item.Id}";
        SelectedItemDescription.Text = "Loading item details…";
        CalculationSummary.Text = "Choose a quantity and calculate the recipe.";
        CalculationWorkbenchText.Text = string.Empty;
        ItemDetailsTabs.Items.Clear();
        ClearCalculation();
        ErrorText.Visibility = Visibility.Collapsed;

        _detailsLoad?.Cancel();
        _detailsLoad?.Dispose();
        var cancellation = new CancellationTokenSource();
        _detailsLoad = cancellation;

        try
        {
            await _apiService.InitializeAsync(RustHelpBrowser, cancellation.Token);
            var details = await _apiService.GetItemDetailsAsync(
                choice.Item.PageSlug,
                cancellationToken: cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_selectedItem, choice.Item))
            {
                return;
            }

            if (!string.Equals(
                    details.Item.ShortName,
                    choice.Item.ShortName,
                    StringComparison.OrdinalIgnoreCase)
                || details.Item.Id != choice.Item.Id)
            {
                throw new InvalidDataException(
                    $"RustHelp's page for {choice.Item.DisplayName} does not match its catalog entry.");
            }

            _selectedDetails = details;
            CalculateButton.IsEnabled = details.Item.Craft is not null;
            SelectedItemImage.Source = new BitmapImage(new Uri(choice.Item.IconUrl, UriKind.Absolute));
            SelectedItemName.Text = details.Item.Name;
            SelectedItemDetails.Text = $"{details.Item.ShortName} · ID {details.Item.Id}";
            SelectedItemDescription.Text = "Open a section below for recipes, research costs, loot, and raid data.";
            ApplyItemSections(details);
            CalculationSummary.Text = details.Item.Craft is { } craft
                ? $"Recipe output: {craft.Amount:N0} per craft"
                : "This item has no crafting recipe listed.";

            if (details.Item.Craft is { } recipe)
            {
                var workbenchText = recipe.Workbench == 0
                    ? "No workbench requirement listed"
                    : $"Workbench tier {recipe.Workbench}";
                CalculationWorkbenchText.Text = workbenchText
                    + (recipe.TimeSec is null ? string.Empty : $" · {recipe.TimeSec:N0} sec per craft");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidDataException
                or IOException
                or JsonException
                or InvalidOperationException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                SelectedItemDescription.Text = "Details could not be loaded.";
                ShowError($"Could not load item details: {exception.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_detailsLoad, cancellation))
            {
                _detailsLoad = null;
            }

            cancellation.Dispose();
        }
    }

    private async void CalculateButton_Click(object sender, RoutedEventArgs e) =>
        await CalculateSelectedItemAsync();

    private async Task RefreshCatalogAsync()
    {
        RefreshButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        DataStatusText.Text = "Loading the RustHelp item catalog…";
        try
        {
            await _apiService.InitializeAsync(RustHelpBrowser);
            var catalog = await _apiService.DownloadLatestCatalogAsync();
            _catalog = catalog;
            ApplyCatalog(catalog);

            try
            {
                await _apiService.SaveCatalogAsync(catalog);
                DataStatusText.Text = FormatSourceStatus(catalog, "RustHelp catalog");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DataStatusText.Text = FormatSourceStatus(catalog, "RustHelp · cache save failed");
                ShowError($"Items were updated but could not be cached for offline use: {exception.Message}");
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidDataException
                or IOException
                or JsonException
                or InvalidOperationException)
        {
            if (_catalog is not null)
            {
                DataStatusText.Text = FormatSourceStatus(_catalog, "Saved RustHelp catalog · update failed");
                ShowError($"Using the saved item catalog. The latest update failed: {exception.Message}");
            }
            else
            {
                DataStatusText.Text = "RustHelp item data is unavailable.";
                ShowError($"Could not load item data and no saved copy is available: {exception.Message}");
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplyCatalog(RustHelpCatalog catalog)
    {
        _itemsByPageSlug = catalog.Items
            .GroupBy(item => item.PageSlug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var choices = catalog.Items
            .Select(item => new RustHelpItemChoice(
                item,
                _preferences.FavoriteCraftingShortNames.Contains(GetFavoriteKey(item), StringComparer.OrdinalIgnoreCase)))
            .ToDictionary(item => item.Item.ShortName, StringComparer.OrdinalIgnoreCase);

        _choiceByShortName = choices;
        UpdateSearchResults();
    }

    private void ApplyItemSections(RustHelpItemDetailsResult details)
    {
        ItemDetailsTabs.Items.Clear();
        AddItemDetailsTab(CreateOverviewSection(details));

        var sections = details.Item.Sections
            .Select(section => new RustHelpItemSection
            {
                Name = section.Name,
                Description = section.Description,
                Rows = section.Rows.ToList(),
            })
            .ToList();

        var craftSection = sections.FirstOrDefault(section =>
            string.Equals(section.Name, "Crafting", StringComparison.OrdinalIgnoreCase));
        if (craftSection is null && details.Item.Craft is not null)
        {
            craftSection = new RustHelpItemSection { Name = "Crafting" };
            sections.Add(craftSection);
        }

        if (craftSection is not null && details.Item.Craft is { } craft)
        {
            craftSection.Rows.Insert(0, CreateRecipeSectionRow(details.Item, craft));
        }

        var lootSection = sections.FirstOrDefault(section =>
            string.Equals(section.Name, "Looted From", StringComparison.OrdinalIgnoreCase));
        if (details.Item.LootedFrom.Count > 0
            && (lootSection is null || lootSection.Rows.Count == 0))
        {
            var fallbackLootSection = new RustHelpItemSection
            {
                Name = "Looted From",
                Rows = details.Item.LootedFrom.Select(source => new RustHelpSectionRow
                {
                    Title = source.Name,
                    IconUrl = GetIconUrl(RustHelpSlug.Create(source.Name)),
                    Cells =
                    [
                        new RustHelpSectionCell { Label = "Chance", Text = source.Chance },
                        new RustHelpSectionCell { Label = "Amount", Text = source.Amount },
                    ],
                }).ToList(),
            };
            if (lootSection is null)
            {
                sections.Add(fallbackLootSection);
            }
            else
            {
                sections[sections.IndexOf(lootSection)] = fallbackLootSection;
            }
        }

        foreach (var section in sections)
        {
            AddItemDetailsTab(section);
        }
    }

    private RustHelpItemSection CreateOverviewSection(RustHelpItemDetailsResult details)
    {
        var overview = new RustHelpItemSection
        {
            Name = "Overview",
            Description = details.Item.Description,
        };
        var sourceStatus = FormatSourceStatus(
            details,
            details.IsCached ? "Cached RustHelp page" : "RustHelp page");
        overview.Rows.Add(new RustHelpSectionRow
        {
            Title = "Item details",
            IconUrl = _selectedItem?.IconUrl ?? string.Empty,
            Cells =
            [
                new RustHelpSectionCell { Label = "Short name", Text = details.Item.ShortName },
                new RustHelpSectionCell
                {
                    Label = "Item ID",
                    Text = details.Item.Id?.ToString("N0") ?? "Not listed",
                },
                new RustHelpSectionCell { Label = "Source", Text = sourceStatus },
            ],
        });

        if (details.Item.Blueprint is { } blueprint)
        {
            var cells = new List<RustHelpSectionCell>();
            if (blueprint.ResearchScrap is { } researchScrap)
            {
                cells.Add(new RustHelpSectionCell
                {
                    Label = "Research table",
                    Entries =
                    [
                        new RustHelpSectionEntry
                        {
                            Name = "Scrap",
                            Quantity = $"×{researchScrap:N0}",
                            IconUrl = GetIconUrl("scrap"),
                        },
                    ],
                });
            }

            if (blueprint.TechTreeBlueprintPages is { } blueprintPages)
            {
                cells.Add(new RustHelpSectionCell
                {
                    Label = "Tech tree",
                    Entries =
                    [
                        new RustHelpSectionEntry
                        {
                            Name = "Blueprint Page",
                            Quantity = $"×{blueprintPages:N0}",
                            IconUrl = GetIconUrl("blueprint-page"),
                        },
                    ],
                });
            }

            if (blueprint.Workbench is { } workbench)
            {
                cells.Add(new RustHelpSectionCell
                {
                    Label = "Workbench",
                    Text = $"Tier {workbench}",
                    Entries =
                    [
                        new RustHelpSectionEntry
                        {
                            Name = $"Workbench Level {workbench}",
                            IconUrl = GetIconUrl($"workbench-level-{workbench}"),
                        },
                    ],
                });
            }

            overview.Rows.Add(new RustHelpSectionRow
            {
                Title = "Blueprint requirements",
                IconUrl = _selectedItem?.IconUrl ?? string.Empty,
                Cells = cells,
            });
        }

        if (!string.IsNullOrWhiteSpace(details.Item.VerifiedAt))
        {
            overview.Rows.Add(new RustHelpSectionRow
            {
                Title = "Data freshness",
                Cells =
                [
                    new RustHelpSectionCell
                    {
                        Label = "Last verified",
                        Text = details.Item.VerifiedAt,
                    },
                ],
            });
        }

        return overview;
    }

    private RustHelpSectionRow CreateRecipeSectionRow(
        RustHelpItemDetails item,
        RustHelpCraftInfo craft) =>
        new()
        {
            Title = "Crafting recipe",
            IconUrl = GetIconUrl(item.PageSlug),
            Cells =
            [
                new RustHelpSectionCell
                {
                    Label = "Ingredients",
                    Entries = craft.Ingredients.Select(ingredient => new RustHelpSectionEntry
                    {
                        Name = ingredient.Name,
                        Quantity = $"×{ingredient.Qty:N0}",
                        IconUrl = GetIconUrl(ingredient.PageSlug),
                    }).ToList(),
                },
                new RustHelpSectionCell
                {
                    Label = "Output",
                    Entries =
                    [
                        new RustHelpSectionEntry
                        {
                            Name = item.Name,
                            Quantity = $"×{craft.Amount:N0}",
                            IconUrl = GetIconUrl(item.PageSlug),
                        },
                    ],
                },
                new RustHelpSectionCell
                {
                    Label = "Craft requirements",
                    Text = (craft.Workbench == 0
                        ? "No workbench requirement listed"
                        : $"Workbench tier {craft.Workbench}")
                        + (craft.TimeSec is null ? string.Empty : $" · {craft.TimeSec:N0} sec per craft"),
                },
            ],
        };

    private void AddItemDetailsTab(RustHelpItemSection section)
    {
        ItemDetailsTabs.Items.Add(new TabItem
        {
            Header = section.Name,
            Content = section,
            ContentTemplate = (DataTemplate)FindResource("RustHelpSectionTemplate"),
        });
    }

    private string GetIconUrl(string pageSlug) =>
        _itemsByPageSlug is not null
        && _itemsByPageSlug.TryGetValue(pageSlug, out var item)
            ? item.IconUrl
            : string.Empty;

    private string ResolveIconUrl(string identifier)
    {
        if (_itemsByPageSlug is null)
        {
            return string.Empty;
        }

        if (_itemsByPageSlug.TryGetValue(identifier, out var item))
        {
            return item.IconUrl;
        }

        return _itemsByPageSlug.Values
            .FirstOrDefault(candidate => string.Equals(
                candidate.ShortName,
                identifier,
                StringComparison.OrdinalIgnoreCase))
            ?.IconUrl ?? string.Empty;
    }

    private Dictionary<string, RustHelpItemChoice>? _choiceByShortName;

    private void UpdateSearchResults()
    {
        _searchResults.Clear();
        _favoriteResults.Clear();
        if (_catalog is null || _choiceByShortName is null)
        {
            SearchResultsText.Text = "ITEMS";
            FavoriteStatusText.Text = "FAVORITES";
            FavoriteEmptyText.Text = "Load item data to see favorites.";
            FavoriteEmptyText.Visibility = Visibility.Visible;
            return;
        }

        var query = SearchBox.Text.Trim();
        var matchingChoices = _choiceByShortName.Values.Where(choice =>
            string.IsNullOrEmpty(query)
            || choice.Item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || choice.Item.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || choice.Item.PageSlug.Contains(query, StringComparison.OrdinalIgnoreCase));
        var matches = matchingChoices
            .OrderBy(choice => choice.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumSearchResults)
            .ToArray();
        foreach (var choice in matches)
        {
            _searchResults.Add(choice);
        }

        var favorites = _choiceByShortName.Values
            .Where(choice => choice.IsFavorite
                && (string.IsNullOrEmpty(query)
                    || choice.Item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || choice.Item.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || choice.Item.PageSlug.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(choice => choice.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        foreach (var choice in favorites)
        {
            _favoriteResults.Add(choice);
        }

        SearchResultsText.Text = string.IsNullOrEmpty(query)
            ? $"ITEMS · SHOWING FIRST {_searchResults.Count}"
            : $"SEARCH RESULTS · {_searchResults.Count}";
        FavoriteStatusText.Text = $"FAVORITES · {_favoriteResults.Count}";
        FavoriteEmptyText.Text = string.IsNullOrEmpty(query)
            ? "Star an item to add it to Favorites."
            : "No favorites match this search.";
        FavoriteEmptyText.Visibility = _favoriteResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CalculateSelectedItemAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (_selectedDetails is null || _itemsByPageSlug is null)
        {
            ShowError("Select an item and wait for its details to load first.");
            return;
        }

        if (_selectedDetails.Item.Craft is null)
        {
            ClearCalculation();
            ShowError("The selected item has no crafting recipe listed by RustHelp.");
            return;
        }

        if (!long.TryParse(QuantityBox.Text, out var requestedAmount)
            || requestedAmount is < 1 or > MaximumRequestedAmount)
        {
            ShowError($"Enter a whole number between 1 and {MaximumRequestedAmount:N0}.");
            return;
        }

        _calculationLoad?.Cancel();
        _calculationLoad?.Dispose();
        var cancellation = new CancellationTokenSource();
        _calculationLoad = cancellation;
        ClearCalculation();
        CalculationSummary.Text = "Calculating recipe ingredients…";
        var result = new CraftingCalculation { RequestedAmount = requestedAmount };
        try
        {
            await ExpandRecipeAsync(
                _selectedDetails.Item,
                requestedAmount,
                0,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                result,
                cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            result.ProducedAmount = result.Steps[0].ProducedAmount;
            foreach (var step in result.Steps)
            {
                _craftSteps.Add(new CraftingStepRow(step, ResolveIconUrl(step.ShortName)));
            }

            foreach (var material in result.Materials.Values.OrderBy(
                item => item.DisplayName,
                StringComparer.CurrentCultureIgnoreCase))
            {
                _materials.Add(new CraftingMaterialRow(material, ResolveIconUrl(material.ShortName)));
            }

            CalculationSummary.Text =
                $"Requested {result.RequestedAmount:N0} · craft output {result.ProducedAmount:N0}";
            CalculationWorkbenchText.Text = result.WorkbenchLevelRequired == 0
                ? "No workbench requirement listed in this recipe tree."
                : $"Workbench tier {result.WorkbenchLevelRequired} required in this recipe tree.";
            MaterialsSourceText.Text =
                "Totals recursively expand RustHelp recipes; items without a listed recipe remain base materials.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or InvalidOperationException
                or OverflowException
                or HttpRequestException
                or TaskCanceledException
                or IOException
                or JsonException)
        {
            ClearCalculation();
            ShowError($"Could not calculate this recipe: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_calculationLoad, cancellation))
            {
                _calculationLoad = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task ExpandRecipeAsync(
        RustHelpItemDetails item,
        long requestedAmount,
        int depth,
        HashSet<string> recipePath,
        CraftingCalculation result,
        CancellationToken cancellationToken)
    {
        if (depth > MaximumRecipeDepth)
        {
            throw new InvalidDataException("The recipe tree is unexpectedly deep and cannot be calculated.");
        }

        if (item.Craft is not { } recipe)
        {
            AddMaterial(result, item.ShortName, item.Name, requestedAmount);
            return;
        }

        if (!recipePath.Add(item.PageSlug))
        {
            throw new InvalidDataException($"The recipe data contains a circular dependency at {item.Name}.");
        }

        try
        {
            var itemCatalog = _itemsByPageSlug
                ?? throw new InvalidOperationException("The RustHelp item catalog is not loaded.");
            var amountPerCraft = recipe.Amount;
            if (amountPerCraft <= 0)
            {
                throw new InvalidDataException($"The recipe for {item.Name} has an invalid output quantity.");
            }

            var batches = DivideRoundUp(requestedAmount, amountPerCraft);
            var producedAmount = checked(batches * amountPerCraft);
            result.Steps.Add(new CraftingStep(
                item.ShortName,
                item.Name,
                batches,
                producedAmount,
                recipe.Workbench,
                depth));
            result.WorkbenchLevelRequired = Math.Max(result.WorkbenchLevelRequired, recipe.Workbench);

            foreach (var ingredient in recipe.Ingredients)
            {
                var needed = checked(ingredient.Qty * batches);
                if (itemCatalog.TryGetValue(ingredient.PageSlug, out var summary))
                {
                    var details = await _apiService.GetItemDetailsAsync(
                        ingredient.PageSlug,
                        cancellationToken: cancellationToken);
                    if (!string.Equals(
                            details.Item.ShortName,
                            summary.ShortName,
                            StringComparison.OrdinalIgnoreCase)
                        || details.Item.Id != summary.Id)
                    {
                        throw new InvalidDataException(
                            $"RustHelp's page for {summary.DisplayName} does not match its catalog entry.");
                    }

                    if (details.Item.Craft is not null)
                    {
                        await ExpandRecipeAsync(
                            details.Item,
                            needed,
                            depth + 1,
                            recipePath,
                            result,
                            cancellationToken);
                    }
                    else
                    {
                        AddMaterial(result, ingredient.PageSlug, ingredient.Name, needed);
                    }
                }
                else
                {
                    AddMaterial(result, ingredient.PageSlug, ingredient.Name, needed);
                }
            }
        }
        finally
        {
            recipePath.Remove(item.PageSlug);
        }
    }

    private static void AddMaterial(CraftingCalculation result, string slug, string name, long amount)
    {
        if (result.Materials.TryGetValue(slug, out var existing))
        {
            result.Materials[slug] = existing with { Amount = checked(existing.Amount + amount) };
        }
        else
        {
            result.Materials.Add(slug, new CraftingMaterial(slug, name, amount));
        }
    }

    private void ClearCalculation()
    {
        _craftSteps.Clear();
        _materials.Clear();
        MaterialsSourceText.Text = string.Empty;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string FormatSourceStatus(RustHelpCatalog catalog, string source) =>
        $"{source} · {catalog.Items.Count:N0} items · fetched {catalog.FetchedAtUtc.ToLocalTime():g}";

    private static string FormatSourceStatus(RustHelpItemDetailsResult details, string source)
    {
        var verified = string.IsNullOrWhiteSpace(details.Item.VerifiedAt)
            ? $"fetched {details.FetchedAtUtc.ToLocalTime():g}"
            : $"verified {details.Item.VerifiedAt}";
        return $"{source} · {verified}";
    }

    private static string GetFavoriteKey(RustHelpItemSummary item) =>
        string.IsNullOrWhiteSpace(item.ShortName) ? item.PageSlug : item.ShortName;

    private static long DivideRoundUp(long value, long divisor) =>
        value / divisor + (value % divisor == 0 ? 0 : 1);

    private sealed class RustHelpItemChoice : INotifyPropertyChanged
    {
        private bool _isFavorite;

        public RustHelpItemChoice(RustHelpItemSummary item, bool isFavorite)
        {
            Item = item;
            _isFavorite = isFavorite;
            var initials = new List<char>();
            foreach (var word in item.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrEmpty(word))
                {
                    initials.Add(char.ToUpperInvariant(word[0]));
                }
            }

            Initials = new string(initials.Take(2).ToArray());
        }

        public RustHelpItemSummary Item { get; }
        public string Initials { get; }
        public string FavoriteGlyph => IsFavorite ? "★" : "☆";
        public string FavoriteToolTip => IsFavorite ? "Remove from Favorites" : "Add to Favorites";

        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                if (_isFavorite == value)
                {
                    return;
                }

                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteToolTip));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class CraftingStepRow
    {
        public CraftingStepRow(CraftingStep step, string iconUrl)
        {
            IndentedName = $"{new string(' ', step.Depth * 3)}{step.DisplayName}";
            BatchText = $"× {step.Batches:N0} → {step.ProducedAmount:N0}";
            WorkbenchText = step.WorkbenchLevelRequired == 0
                ? "No workbench listed"
                : $"Workbench tier {step.WorkbenchLevelRequired}";
            IconUrl = iconUrl;
            Initials = new string(step.DisplayName
                .Where(char.IsLetterOrDigit)
                .Take(2)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        public string IndentedName { get; }
        public string BatchText { get; }
        public string WorkbenchText { get; }
        public string IconUrl { get; }
        public string Initials { get; }
    }

    private sealed class CraftingMaterialRow
    {
        public CraftingMaterialRow(CraftingMaterial material, string iconUrl)
        {
            Name = material.DisplayName;
            AmountText = material.Amount.ToString("N0");
            IconUrl = iconUrl;
            Initials = new string(material.DisplayName
                .Where(char.IsLetterOrDigit)
                .Take(2)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        public string Name { get; }
        public string AmountText { get; }
        public string IconUrl { get; }
        public string Initials { get; }
    }
}
