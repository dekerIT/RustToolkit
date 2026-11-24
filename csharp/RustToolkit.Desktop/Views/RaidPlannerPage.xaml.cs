using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using RustToolkit.Desktop.Models;
using RustToolkit.Desktop.Services;

namespace RustToolkit.Desktop.Views;

public partial class RaidPlannerPage : System.Windows.Controls.UserControl
{
    private readonly RustlyApiService _apiService;
    private readonly RustHelpItemService _rustHelpItemService;
    private readonly ObservableCollection<RaidMethodRow> _methods = [];
    private Dictionary<string, RustHelpItemSummary> _itemsByPageSlug = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, RustHelpItemSummary> _itemsByShortName = new(StringComparer.OrdinalIgnoreCase);
    private RustlyRaidTargetCatalog? _targetCatalog;
    private RustlyRaidTarget? _selectedTarget;
    private RustlyRaidPlanResult? _currentPlan;
    private CancellationTokenSource? _planLoad;
    private bool _isInitializing = true;
    private bool _initialized;
    private bool _changingSelection;

    public RaidPlannerPage(RustlyApiService apiService, RustHelpItemService rustHelpItemService)
    {
        InitializeComponent();
        _isInitializing = false;
        _apiService = apiService;
        _rustHelpItemService = rustHelpItemService;
        MethodsList.ItemsSource = _methods;
    }

    private async void RaidPlannerPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadItemIconsAsync();
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await LoadTargetsAsync(forceRefresh: false);
    }

    private async Task LoadItemIconsAsync()
    {
        try
        {
            var catalog = await _rustHelpItemService.LoadCachedCatalogAsync();
            if (catalog is null)
            {
                ItemIconStatusText.Text = "Open Crafting once to cache RustHelp item images.";
                return;
            }

            _itemsByPageSlug = catalog.Items
                .GroupBy(item => item.PageSlug, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _itemsByShortName = catalog.Items
                .ToDictionary(item => item.ShortName, StringComparer.OrdinalIgnoreCase);
            ItemIconStatusText.Text = $"Item images · {catalog.Items.Count:N0} RustHelp items";
            UpdateSelectedTargetImage();
            Recalculate();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            ItemIconStatusText.Text = $"RustHelp item images unavailable: {exception.Message}";
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await LoadTargetsAsync(forceRefresh: true);

    private async void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingSelection || TargetComboBox.SelectedItem is not RustlyRaidTarget target)
        {
            return;
        }

        _selectedTarget = target;
        CurrentHealthBox.Text = target.Hp.ToString();
        SoftSideCheckBox.IsChecked = false;
        SoftSideCheckBox.Visibility = target.HasSoftSide ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectedTargetImage();
        OpenSourceButton.IsEnabled = true;
        await LoadPlanAsync(forceRefresh: false);
    }

    private async void SoftSideCheckBox_Click(object sender, RoutedEventArgs e) =>
        await LoadPlanAsync(forceRefresh: false);

    private void Inputs_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializing)
        {
            Recalculate();
        }
    }

    private void OwnedCountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializing)
        {
            Recalculate();
        }
    }

    private async Task LoadTargetsAsync(bool forceRefresh)
    {
        RefreshButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        SourceStatusText.Text = "Loading raid targets from Rustly…";

        try
        {
            var previousSlug = _selectedTarget?.Slug;
            _targetCatalog = await _apiService.GetRaidTargetsAsync(forceRefresh);
            SourceStatusText.Text = FormatSourceStatus(
                _targetCatalog.Metadata,
                _targetCatalog.IsCached ? "Cached Rustly API" : "Rustly API");

            var targets = _targetCatalog.Targets
                .OrderBy(target => target.Kind, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(target => target.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            _changingSelection = true;
            TargetComboBox.ItemsSource = targets;
            TargetComboBox.SelectedItem = targets.FirstOrDefault(target =>
                string.Equals(target.Slug, previousSlug, StringComparison.OrdinalIgnoreCase))
                ?? targets.FirstOrDefault(target =>
                    string.Equals(target.Slug, "sheet-metal-door", StringComparison.OrdinalIgnoreCase))
                ?? targets.FirstOrDefault();
            _changingSelection = false;

            if (TargetComboBox.SelectedItem is RustlyRaidTarget target)
            {
                _selectedTarget = target;
                CurrentHealthBox.Text = target.Hp.ToString();
                SoftSideCheckBox.IsChecked = false;
                SoftSideCheckBox.Visibility = target.HasSoftSide ? Visibility.Visible : Visibility.Collapsed;
                UpdateSelectedTargetImage();
                OpenSourceButton.IsEnabled = true;
                await LoadPlanAsync(forceRefresh);
            }
        }

        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidDataException
                or IOException
                or JsonException)
        {
            SourceStatusText.Text = "Rustly raid data is unavailable.";
            ShowError($"Could not load raid targets: {exception.Message}");
        }
        finally
        {
            _changingSelection = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void UpdateSelectedTargetImage()
    {
        if (_selectedTarget is null)
        {
            SelectedTargetGlyph.Text = string.Empty;
            SelectedTargetImage.Source = null;
            return;
        }

        var pageSlug = RustHelpSlug.Create(_selectedTarget.Name);
        var iconUrl = _itemsByPageSlug.TryGetValue(pageSlug, out var item)
            ? item.IconUrl
            : string.Empty;
        SelectedTargetGlyph.Text = new string(_selectedTarget.Name
            .Where(char.IsLetterOrDigit)
            .Take(2)
            .Select(char.ToUpperInvariant)
            .ToArray());
        SelectedTargetImage.Source = string.IsNullOrWhiteSpace(iconUrl)
            ? null
            : new System.Windows.Media.Imaging.BitmapImage(new Uri(iconUrl, UriKind.Absolute));
    }

    private async Task LoadPlanAsync(bool forceRefresh)
    {
        if (_selectedTarget is null)
        {
            return;
        }

        _planLoad?.Cancel();
        _planLoad?.Dispose();
        var cancellation = new CancellationTokenSource();
        _planLoad = cancellation;
        _currentPlan = null;
        _methods.Clear();
        ErrorText.Visibility = Visibility.Collapsed;
        PlanSummaryText.Text = "Loading the Rustly raid plan…";

        try
        {
            var result = await _apiService.GetRaidPlanAsync(
                _selectedTarget.Slug,
                SoftSideCheckBox.IsChecked == true,
                forceRefresh,
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || !string.Equals(
                    result.Plan.Target.Slug,
                    _selectedTarget.Slug,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentPlan = result;
            SourceStatusText.Text = FormatSourceStatus(
                result.Metadata,
                result.IsCached ? "Cached Rustly API" : "Rustly API");
            PlanSummaryText.Text =
                $"{result.Plan.Target.Name} · {result.Plan.Target.Hp:N0} base HP · {result.Plan.Side} side";
            Recalculate();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
                or InvalidDataException
                or IOException
                or JsonException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                PlanSummaryText.Text = "Raid plan could not be loaded.";
                ShowError($"Could not load the Rustly raid plan: {exception.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_planLoad, cancellation))
            {
                _planLoad = null;
            }

            cancellation.Dispose();
        }
    }

    private void Recalculate()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (_currentPlan is null)
        {
            return;
        }

        if (!int.TryParse(TargetCountBox.Text, out var targetCount) || targetCount is < 1 or > 1_000)
        {
            InvalidateEstimates();
            ShowError("Enter a target count from 1 to 1,000.");
            return;
        }

        if (!int.TryParse(CurrentHealthBox.Text, out var currentHealth)
            || currentHealth is < 1
            || currentHealth > _currentPlan.Plan.Target.Hp)
        {
            InvalidateEstimates();
            ShowError($"Enter current HP from 1 to {_currentPlan.Plan.Target.Hp:N0}.");
            return;
        }

        var ownedCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _methods)
        {
            if (!long.TryParse(row.OwnedText, out var owned) || owned is < 0 or > 1_000_000_000)
            {
                InvalidateEstimates();
                ShowError("Enter a whole number from 0 to 1,000,000,000 for each owned explosive.");
                return;
            }

            ownedCounts[row.Slug] = owned;
        }

        try
        {
            var estimates = RaidEstimateCalculator.Calculate(
                _currentPlan.Plan,
                currentHealth,
                targetCount,
                ownedCounts);
            var cheapest = estimates.Min(estimate => estimate.Sulfur);
            var fastest = estimates.Min(estimate => estimate.Seconds);
            foreach (var estimate in estimates)
            {
                var row = _methods.FirstOrDefault(candidate =>
                    string.Equals(candidate.Slug, estimate.Method.Slug, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new RaidMethodRow(
                        estimate.Method,
                        GetExplosiveIconUrl(estimate.Method),
                        GetIconUrl("sulfur"),
                        GetIconUrl("charcoal"),
                        GetIconUrl("metal.fragments"));
                    _methods.Add(row);
                }

                row.SetIconUrls(
                    GetExplosiveIconUrl(estimate.Method),
                    GetIconUrl("sulfur"),
                    GetIconUrl("charcoal"),
                    GetIconUrl("metal.fragments"));
                row.Apply(
                    estimate,
                    estimate.Sulfur == cheapest,
                    estimate.Seconds == fastest);
            }

            var targetLabel = targetCount == 1 ? "target" : "targets";
            var softLabel = _currentPlan.Plan.Side.Equals("soft", StringComparison.OrdinalIgnoreCase)
                ? "soft side"
                : "hard side";
            PlanSummaryText.Text =
                $"{_currentPlan.Plan.Target.Name} · {targetCount:N0} {targetLabel} · {currentHealth:N0} HP each · {softLabel}";
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        {
            InvalidateEstimates();
            ShowError($"Could not estimate the raid plan: {exception.Message}");
        }
    }

    private string GetIconUrl(string shortName) =>
        _itemsByShortName.TryGetValue(shortName, out var item)
            ? item.IconUrl
            : string.Empty;

    private string GetExplosiveIconUrl(RustlyExplosiveMethod method)
    {
        var value = $"{method.Slug} {method.Name}".ToLowerInvariant();
        var shortName = value.Contains("5.56", StringComparison.Ordinal)
                || value.Contains("556", StringComparison.Ordinal)
                || value.Contains("explosive ammo", StringComparison.Ordinal)
            ? "ammo.rifle.explosive"
            : value.Contains("incendiary", StringComparison.Ordinal)
                && value.Contains("rocket", StringComparison.Ordinal)
                ? "rocket.fire"
                : value.Contains("high velocity", StringComparison.Ordinal)
                    || value.Contains("hv rocket", StringComparison.Ordinal)
                ? "rocket.hv"
                : value.Contains("satchel", StringComparison.Ordinal)
                    ? "explosive.satchel"
                    : value.Contains("timed", StringComparison.Ordinal)
                        || value.Contains("c4", StringComparison.Ordinal)
                    ? "explosive.timed"
                    : value.Contains("beancan", StringComparison.Ordinal)
                        ? "grenade.beancan"
                        : value.Contains("propane", StringComparison.Ordinal)
                            ? "grenade.propane"
                            : value.Contains("rocket", StringComparison.Ordinal)
                                ? "rocket.basic"
                                : string.Empty;

        if (!string.IsNullOrEmpty(shortName))
        {
            var iconUrl = GetIconUrl(shortName);
            if (!string.IsNullOrEmpty(iconUrl))
            {
                return iconUrl;
            }
        }

        var pageSlug = RustHelpSlug.Create(method.Name);
        if (_itemsByPageSlug.TryGetValue(pageSlug, out var exactMatch))
        {
            return exactMatch.IconUrl;
        }

        return _itemsByPageSlug.Values
            .FirstOrDefault(item => item.DisplayName.Contains(method.Name, StringComparison.OrdinalIgnoreCase))
            ?.IconUrl ?? string.Empty;
    }

    private void InvalidateEstimates()
    {
        foreach (var row in _methods)
        {
            row.ClearEstimate();
        }
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan is null)
        {
            return;
        }

        const string url = "https://rustly.com/developers/api/";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowError($"Could not open the Rustly source page: {exception.Message}");
        }
    }

    private static string FormatSourceStatus(RustlyApiMetadata metadata, string source)
    {
        var verified = string.IsNullOrWhiteSpace(metadata.VerifiedAt)
            ? "verification date unavailable"
            : $"verified {metadata.VerifiedAt}";
        var gameVersion = string.IsNullOrWhiteSpace(metadata.GameVersion)
            ? "game version unavailable"
            : $"game {metadata.GameVersion}";
        return $"{source} · {gameVersion} · {verified}";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private sealed class RaidMethodRow : INotifyPropertyChanged
    {
        private string _ownedText = "0";
        private string _badges = string.Empty;
        private string _requiredText = "—";
        private string _bringText = "—";
        private string _iconUrl;
        private string _sulfurIconUrl;
        private string _charcoalIconUrl;
        private string _metalFragmentsIconUrl;
        private string _sulfurText = "—";
        private string _charcoalText = "—";
        private string _metalFragmentsText = "—";
        private string _timeText = "—";
        private string _estimateFootnote = "Enter valid inputs to calculate.";

        public RaidMethodRow(
            RustlyExplosiveMethod method,
            string iconUrl,
            string sulfurIconUrl,
            string charcoalIconUrl,
            string metalFragmentsIconUrl)
        {
            Slug = method.Slug;
            Name = method.Name;
            _iconUrl = iconUrl;
            _sulfurIconUrl = sulfurIconUrl;
            _charcoalIconUrl = charcoalIconUrl;
            _metalFragmentsIconUrl = metalFragmentsIconUrl;
        }

        public string Slug { get; }
        public string Name { get; }
        public string Initials => new(Name
            .Where(char.IsLetterOrDigit)
            .Take(2)
            .Select(char.ToUpperInvariant)
            .ToArray());
        public RustlyRaidEstimate? Estimate { get; private set; }
        public string IconUrl { get => _iconUrl; private set => SetProperty(ref _iconUrl, value); }
        public string SulfurIconUrl { get => _sulfurIconUrl; private set => SetProperty(ref _sulfurIconUrl, value); }
        public string CharcoalIconUrl { get => _charcoalIconUrl; private set => SetProperty(ref _charcoalIconUrl, value); }
        public string MetalFragmentsIconUrl { get => _metalFragmentsIconUrl; private set => SetProperty(ref _metalFragmentsIconUrl, value); }
        public string SulfurText { get => _sulfurText; private set => SetProperty(ref _sulfurText, value); }
        public string CharcoalText { get => _charcoalText; private set => SetProperty(ref _charcoalText, value); }
        public string MetalFragmentsText { get => _metalFragmentsText; private set => SetProperty(ref _metalFragmentsText, value); }
        public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
        public string EstimateFootnote { get => _estimateFootnote; private set => SetProperty(ref _estimateFootnote, value); }

        public string OwnedText
        {
            get => _ownedText;
            set => SetProperty(ref _ownedText, value);
        }

        public string Badges
        {
            get => _badges;
            private set => SetProperty(ref _badges, value);
        }

        public string RequiredText
        {
            get => _requiredText;
            private set => SetProperty(ref _requiredText, value);
        }

        public string BringText
        {
            get => _bringText;
            private set => SetProperty(ref _bringText, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetIconUrls(
            string iconUrl,
            string sulfurIconUrl,
            string charcoalIconUrl,
            string metalFragmentsIconUrl)
        {
            IconUrl = iconUrl;
            SulfurIconUrl = sulfurIconUrl;
            CharcoalIconUrl = charcoalIconUrl;
            MetalFragmentsIconUrl = metalFragmentsIconUrl;
        }

        public void Apply(RustlyRaidEstimate estimate, bool cheapest, bool fastest)
        {
            Estimate = estimate;
            RequiredText = $"{estimate.RequiredAmount:N0}";
            BringText = $"{estimate.BringAmount:N0}";
            SulfurText = estimate.Sulfur.ToString("N0");
            CharcoalText = estimate.Charcoal.ToString("N0");
            MetalFragmentsText = estimate.MetalFragments.ToString("N0");
            TimeText = FormatDuration(estimate.Seconds);
            EstimateFootnote =
                $"About {estimate.EstimatedRemainingHp:N0} HP estimated after using owned explosives.";
            var badges = new List<string>();
            if (cheapest)
            {
                badges.Add("LOWEST SULFUR");
            }

            if (fastest)
            {
                badges.Add("FASTEST");
            }

            Badges = string.Join("  ·  ", badges);
            OnPropertyChanged(nameof(Estimate));
        }

        private static string FormatDuration(long seconds)
        {
            var hours = seconds / 3_600;
            var minutes = seconds % 3_600 / 60;
            var remainingSeconds = seconds % 60;
            return hours > 0
                ? $"{hours}:{minutes:00}:{remainingSeconds:00}"
                : $"{minutes}:{remainingSeconds:00}";
        }

        public void ClearEstimate()
        {
            Estimate = null;
            RequiredText = "—";
            BringText = "—";
            SulfurText = "—";
            CharcoalText = "—";
            MetalFragmentsText = "—";
            TimeText = "—";
            Badges = string.Empty;
            EstimateFootnote = "Enter valid inputs to calculate.";
            OnPropertyChanged(nameof(Estimate));
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
