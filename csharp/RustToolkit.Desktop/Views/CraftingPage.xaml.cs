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

    private readonly CraftingDataService _dataService;
    private readonly AppPreferencesStore _preferencesStore;
    private readonly AppPreferences _preferences;
    private readonly CraftingCalculator _calculator = new();
    private readonly ObservableCollection<CraftingItemChoice> _searchResults = [];
    private readonly ObservableCollection<CraftingItemChoice> _favoriteResults = [];
    private readonly ObservableCollection<CraftingStepRow> _craftSteps = [];
    private readonly ObservableCollection<CraftingMaterialRow> _materials = [];
    private CraftingCatalog? _catalog;
    private Dictionary<string, CraftingItem>? _recipes;
    private Dictionary<string, CraftingItemChoice>? _choices;
    private CraftingItem? _selectedItem;
    private CancellationTokenSource? _searchIconLoad;
    private CancellationTokenSource? _selectedIconLoad;
    private bool _initialized;

    public CraftingPage(
        CraftingDataService dataService,
        AppPreferencesStore preferencesStore,
        AppPreferences preferences)
    {
        InitializeComponent();
        _dataService = dataService;
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
            _catalog = await _dataService.LoadCachedCatalogAsync();
            if (_catalog is not null)
            {
                ApplyCatalog(_catalog);
                DataStatusText.Text = $"Saved catalog · {_catalog.Items.Count:N0} recipes";
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            ShowError($"Could not read the saved recipe catalog: {exception.Message}");
        }

        await RefreshCatalogAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await RefreshCatalogAsync();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateSearchResults();

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: CraftingItemChoice choice })
        {
            return;
        }

        choice.IsFavorite = !choice.IsFavorite;
        if (choice.IsFavorite)
        {
            _preferences.FavoriteCraftingShortNames.Add(choice.Item.ShortName);
        }
        else
        {
            _preferences.FavoriteCraftingShortNames.RemoveAll(shortName =>
                string.Equals(shortName, choice.Item.ShortName, StringComparison.OrdinalIgnoreCase));
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

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox
            || listBox.SelectedItem is not CraftingItemChoice choice)
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
        SelectedItemName.Text = choice.Item.DisplayName;
        SelectedItemDetails.Text = $"{choice.Item.CategoryName} · {choice.Item.AmountToCreate} per craft";
        SelectedItemImage.Source = choice.Icon;
        if (_selectedIconLoad is not null)
        {
            var previousSelectedIconLoad = _selectedIconLoad;
            _selectedIconLoad = null;
            previousSelectedIconLoad.Cancel();
        }

        _selectedIconLoad = new CancellationTokenSource();
        _ = LoadSelectedIconAsync(choice, _selectedIconLoad);
        CalculateSelectedItem();
    }

    private void CalculateButton_Click(object sender, RoutedEventArgs e) =>
        CalculateSelectedItem();

    private async Task RefreshCatalogAsync()
    {
        RefreshButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        DataStatusText.Text = "Checking the Rust Items API and downloading recipes…";
        try
        {
            var catalog = await _dataService.DownloadLatestCatalogAsync();
            _catalog = catalog;
            ApplyCatalog(catalog);

            try
            {
                await _dataService.SaveCatalogAsync(catalog);
                DataStatusText.Text = FormatSourceStatus(catalog, "Live API");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DataStatusText.Text = FormatSourceStatus(catalog, "Live API · cache save failed");
                ShowError($"Recipes were updated but could not be cached for offline use: {exception.Message}");
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidDataException
                or IOException
                or JsonException)
        {
            if (_catalog is not null)
            {
                DataStatusText.Text = FormatSourceStatus(_catalog, "Saved copy · update failed");
                ShowError($"Using saved recipes. The latest update failed: {exception.Message}");
            }
            else
            {
                DataStatusText.Text = "Recipe data is unavailable.";
                ShowError($"Could not download recipes and no saved copy is available: {exception.Message}");
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplyCatalog(CraftingCatalog catalog)
    {
        _recipes = catalog.Items.ToDictionary(item => item.ShortName, StringComparer.OrdinalIgnoreCase);
        _choices = catalog.Items.ToDictionary(
            item => item.ShortName,
            item => new CraftingItemChoice(
                item,
                _preferences.FavoriteCraftingShortNames.Contains(item.ShortName, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        UpdateSearchResults();
    }

    private void UpdateSearchResults()
    {
        if (_searchIconLoad is not null)
        {
            var previousSearchIconLoad = _searchIconLoad;
            _searchIconLoad = null;
            previousSearchIconLoad.Cancel();
        }

        var cancellation = new CancellationTokenSource();
        _searchIconLoad = cancellation;

        _searchResults.Clear();
        _favoriteResults.Clear();
        if (_catalog is null || _choices is null)
        {
            SearchResultsText.Text = "CRAFTABLE ITEMS";
            FavoriteStatusText.Text = "FAVORITES";
            FavoriteEmptyText.Text = "Load recipe data to see favorites.";
            FavoriteEmptyText.Visibility = Visibility.Visible;
            _searchIconLoad = null;
            cancellation.Dispose();
            return;
        }

        var query = SearchBox.Text.Trim();
        var matches = _choices.Values
            .Where(choice => string.IsNullOrEmpty(query)
                || choice.Item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || choice.Item.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(choice => choice.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumSearchResults)
            .ToArray();

        foreach (var choice in matches)
        {
            _searchResults.Add(choice);
        }

        var favorites = _choices.Values
            .Where(choice => choice.IsFavorite
                && (string.IsNullOrEmpty(query)
                    || choice.Item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || choice.Item.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(choice => choice.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        foreach (var choice in favorites)
        {
            _favoriteResults.Add(choice);
        }

        SearchResultsText.Text = string.IsNullOrEmpty(query)
            ? $"CRAFTABLE ITEMS · SHOWING FIRST {_searchResults.Count}"
            : $"SEARCH RESULTS · {_searchResults.Count}";
        FavoriteStatusText.Text = $"FAVORITES · {_favoriteResults.Count}";
        FavoriteEmptyText.Text = string.IsNullOrEmpty(query)
            ? "Star an item to add it to Favorites."
            : "No favorites match this search.";
        FavoriteEmptyText.Visibility = _favoriteResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var iconChoices = matches
            .Concat(favorites.Take(MaximumSearchResults))
            .DistinctBy(choice => choice.Item.ShortName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _ = LoadSearchIconsAsync(iconChoices, cancellation);
    }

    private async Task LoadSearchIconsAsync(
        IReadOnlyList<CraftingItemChoice> choices,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(250, cancellation.Token);
            await Task.WhenAll(choices.Select(async choice =>
            {
                var bytes = await _dataService.LoadIconAsync(choice.Item.ShortName, cancellation.Token);
                if (bytes is not null && !cancellation.IsCancellationRequested)
                {
                    choice.Icon = DecodePng(bytes, 44);
                }
            }));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                DataStatusText.Text = "Some item icons could not be loaded.";
                ErrorText.Text = $"Could not load an item icon: {exception.Message}";
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_searchIconLoad, cancellation))
            {
                _searchIconLoad = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task LoadSelectedIconAsync(
        CraftingItemChoice choice,
        CancellationTokenSource cancellation)
    {
        try
        {
            var bytes = choice.Icon is null
                ? await _dataService.LoadIconAsync(choice.Item.ShortName, cancellation.Token)
                : null;
            if (bytes is not null && !cancellation.IsCancellationRequested)
            {
                choice.Icon = DecodePng(bytes, 96);
            }

            if (!cancellation.IsCancellationRequested && ReferenceEquals(_selectedItem, choice.Item))
            {
                SelectedItemImage.Source = choice.Icon
                    ?? (bytes is null ? null : DecodePng(bytes, 96));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                ShowError($"Could not load the selected item image: {exception.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_selectedIconLoad, cancellation))
            {
                _selectedIconLoad = null;
            }

            cancellation.Dispose();
        }
    }

    private void CalculateSelectedItem()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (_selectedItem is null || _recipes is null)
        {
            ShowError("Choose a craftable item from the list first.");
            return;
        }

        if (!long.TryParse(QuantityBox.Text, out var requestedAmount)
            || requestedAmount is < 1 or > MaximumRequestedAmount)
        {
            ShowError($"Enter a whole number between 1 and {MaximumRequestedAmount:N0}.");
            return;
        }

        try
        {
            var calculation = _calculator.Calculate(_recipes, _selectedItem.ShortName, requestedAmount);
            _craftSteps.Clear();
            foreach (var step in calculation.Steps)
            {
                _craftSteps.Add(new CraftingStepRow(step));
            }

            _materials.Clear();
            foreach (var material in calculation.Materials.Values.OrderBy(
                item => item.DisplayName,
                StringComparer.CurrentCultureIgnoreCase))
            {
                _materials.Add(new CraftingMaterialRow(material));
            }

            CalculationSummary.Text =
                $"Requested {calculation.RequestedAmount:N0} · craft output {calculation.ProducedAmount:N0}";
            WorkbenchText.Text = calculation.WorkbenchLevelRequired == 0
                ? "No workbench required for this recipe tree."
                : $"Workbench tier {calculation.WorkbenchLevelRequired} required somewhere in this recipe tree.";
            _ = LoadMaterialIconsAsync(_materials.ToArray());
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException or OverflowException)
        {
            ShowError($"Could not calculate this recipe: {exception.Message}");
        }
    }

    private async Task LoadMaterialIconsAsync(IReadOnlyList<CraftingMaterialRow> materials)
    {
        foreach (var row in materials)
        {
            try
            {
                var bytes = await _dataService.LoadIconAsync(row.Material.ShortName);
                if (bytes is not null)
                {
                    row.Icon = DecodePng(bytes, 40);
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or InvalidDataException)
            {
                if (ReferenceEquals(MaterialsList.ItemsSource, _materials))
                {
                    DataStatusText.Text = $"Some ingredient icons are unavailable: {exception.Message}";
                }
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string FormatSourceStatus(CraftingCatalog catalog, string source)
    {
        var updated = catalog.SourceUpdatedAtUtc?.ToLocalTime().ToString("g")
            ?? catalog.FetchedAtUtc.ToLocalTime().ToString("g");
        return $"{source} · {catalog.Items.Count:N0} recipes · source updated {updated}";
    }

    private static BitmapImage DecodePng(byte[] bytes, int decodePixelWidth)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private sealed class CraftingItemChoice : INotifyPropertyChanged
    {
        private BitmapImage? _icon;
        private bool _isFavorite;

        public CraftingItemChoice(CraftingItem item, bool isFavorite)
        {
            Item = item;
            _isFavorite = isFavorite;
        }

        public CraftingItem Item { get; }
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

        public BitmapImage? Icon
        {
            get => _icon;
            set
            {
                if (_icon == value)
                {
                    return;
                }

                _icon = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class CraftingStepRow
    {
        public CraftingStepRow(CraftingStep step)
        {
            IndentedName = $"{new string(' ', step.Depth * 3)}{step.DisplayName}";
            BatchText = $"× {step.Batches:N0} → {step.ProducedAmount:N0}";
            WorkbenchText = step.WorkbenchLevelRequired == 0
                ? "No workbench"
                : $"Workbench tier {step.WorkbenchLevelRequired}";
        }

        public string IndentedName { get; }
        public string BatchText { get; }
        public string WorkbenchText { get; }
    }

    private sealed class CraftingMaterialRow : INotifyPropertyChanged
    {
        private BitmapImage? _icon;

        public CraftingMaterialRow(CraftingMaterial material)
        {
            Material = material;
            AmountText = material.Amount.ToString("N0");
        }

        public CraftingMaterial Material { get; }
        public string AmountText { get; }

        public BitmapImage? Icon
        {
            get => _icon;
            set
            {
                if (_icon == value)
                {
                    return;
                }

                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
