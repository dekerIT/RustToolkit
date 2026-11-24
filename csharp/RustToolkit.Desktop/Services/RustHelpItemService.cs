using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class RustHelpItemService : IDisposable
{
    private const string RustHelpOrigin = "https://rusthelp.com";
    private const string CatalogPath = "/downloads/admin-item-list-public.json";
    private const int MaximumCatalogSize = 5_000;
    private const long MaximumCatalogBytes = 10 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _browserGate = new(1, 1);
    private readonly string _catalogCachePath;
    private readonly Dictionary<string, RustHelpItemDetailsResult> _itemDetails = new(StringComparer.OrdinalIgnoreCase);
    private WebView2? _browser;
    private CoreWebView2? _core;
    private bool _disposed;

    public RustHelpItemService()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustToolkit",
            "rusthelp");
        _catalogCachePath = Path.Combine(dataDirectory, "items.json");
    }

    public async Task InitializeAsync(WebView2 browser, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(browser);

        await _browserGate.WaitAsync(cancellationToken);
        try
        {
            if (_core is not null)
            {
                if (!ReferenceEquals(_browser, browser))
                {
                    throw new InvalidOperationException("The RustHelp browser is already attached to another control.");
                }

                return;
            }

            _browser = browser;
            var userDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustToolkit",
                "RustHelpWebView");
            Directory.CreateDirectory(userDataPath);
            browser.CreationProperties ??= new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataPath,
                IsInPrivateModeEnabled = true,
            };

            try
            {
                await browser.EnsureCoreWebView2Async();
            }
            catch (WebView2RuntimeNotFoundException exception)
            {
                throw new InvalidOperationException(
                    "The Microsoft Edge WebView2 Runtime is required to read RustHelp item pages.",
                    exception);
            }

            _core = browser.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialized without a browser instance.");
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.AreDevToolsEnabled = false;

            await NavigateAsync($"{RustHelpOrigin}/robots.txt", cancellationToken);
        }
        finally
        {
            _browserGate.Release();
        }
    }

    public async Task<RustHelpCatalog?> LoadCachedCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_catalogCachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_catalogCachePath);
            var catalog = await JsonSerializer.DeserializeAsync<RustHelpCatalog>(
                stream,
                JsonOptions,
                cancellationToken);
            ValidateCatalog(catalog);
            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The saved RustHelp item catalog is invalid and was not changed: {_catalogCachePath}",
                exception);
        }
    }

    public async Task<RustHelpCatalog> DownloadLatestCatalogAsync(CancellationToken cancellationToken = default)
    {
        var core = GetCore();
        await _browserGate.WaitAsync(cancellationToken);
        try
        {
            var json = await FetchSameOriginTextAsync(core, CatalogPath, cancellationToken);
            if (json.Length > MaximumCatalogBytes)
            {
                throw new InvalidDataException("RustHelp returned an oversized item catalog.");
            }

            var items = JsonSerializer.Deserialize<List<RustHelpItemSummary>>(json, JsonOptions);
            var catalog = new RustHelpCatalog
            {
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Items = items ?? [],
            };
            ValidateCatalog(catalog);
            return catalog;
        }
        finally
        {
            _browserGate.Release();
        }
    }

    public async Task SaveCatalogAsync(RustHelpCatalog catalog, CancellationToken cancellationToken = default)
    {
        ValidateCatalog(catalog);
        var directory = Path.GetDirectoryName(_catalogCachePath)
            ?? throw new IOException("Could not determine the RustHelp cache directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_catalogCachePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _catalogCachePath, overwrite: true);
    }

    public async Task<RustHelpItemDetailsResult> GetItemDetailsAsync(
        string pageSlug,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(pageSlug);
        if (!forceRefresh
            && _itemDetails.TryGetValue(pageSlug, out var cached)
            && DateTimeOffset.UtcNow - cached.FetchedAtUtc < CacheLifetime)
        {
            return cached with { IsCached = true };
        }

        var core = GetCore();
        await _browserGate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh
                && _itemDetails.TryGetValue(pageSlug, out cached)
                && DateTimeOffset.UtcNow - cached.FetchedAtUtc < CacheLifetime)
            {
                return cached with { IsCached = true };
            }

            var pageUrl = $"{RustHelpOrigin}/items/{Uri.EscapeDataString(pageSlug)}";
            await NavigateAsync(pageUrl, cancellationToken);
            var scriptResult = await core.ExecuteScriptAsync(ExtractItemPageScript);
            var snapshotJson = JsonSerializer.Deserialize<string>(scriptResult, JsonOptions);
            if (string.IsNullOrWhiteSpace(snapshotJson))
            {
                throw new InvalidDataException($"RustHelp returned no item-page data for '{pageSlug}'.");
            }

            var snapshot = JsonSerializer.Deserialize<RustHelpItemPageSnapshot>(snapshotJson, JsonOptions)
                ?? throw new InvalidDataException($"RustHelp returned invalid item-page data for '{pageSlug}'.");
            var item = BuildItemDetails(snapshot, pageSlug);
            var result = new RustHelpItemDetailsResult(item, DateTimeOffset.UtcNow, IsCached: false);
            _itemDetails[pageSlug] = result;
            return result;
        }
        finally
        {
            _browserGate.Release();
        }
    }

    public static string BuildItemPageUrl(string pageSlug)
    {
        ValidateSlug(pageSlug);
        return $"{RustHelpOrigin}/items/{Uri.EscapeDataString(pageSlug)}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _core?.Stop();
    }

    private CoreWebView2 GetCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _core
            ?? throw new InvalidOperationException("The RustHelp browser has not been initialized.");
    }

    private async Task NavigateAsync(string url, CancellationToken cancellationToken)
    {
        var core = GetCore();
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
            completion.TrySetResult(args);

        core.NavigationCompleted += OnNavigationCompleted;
        try
        {
            core.Navigate(url);
            CoreWebView2NavigationCompletedEventArgs result;
            try
            {
                result = await completion.Task.WaitAsync(BrowserTimeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                core.Stop();
                throw;
            }
            catch (TimeoutException exception)
            {
                core.Stop();
                throw new TimeoutException($"RustHelp did not finish loading '{url}' in time.", exception);
            }

            if (!result.IsSuccess)
            {
                throw new HttpRequestException(
                    $"WebView2 could not load '{url}': {result.WebErrorStatus}.");
            }

            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var currentUri)
                || !string.Equals(currentUri.Host, "rusthelp.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"RustHelp redirected to an unexpected page: {core.Source}");
            }
        }
        finally
        {
            core.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    private async Task<string> FetchSameOriginTextAsync(
        CoreWebView2 core,
        string path,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<RustHelpFetchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using var message = JsonDocument.Parse(args.WebMessageAsJson);
                var root = message.RootElement;
                if (!root.TryGetProperty("requestId", out var idElement)
                    || !string.Equals(idElement.GetString(), requestId, StringComparison.Ordinal))
                {
                    return;
                }

                if (root.TryGetProperty("error", out var errorElement))
                {
                    completion.TrySetException(new HttpRequestException(errorElement.GetString()));
                    return;
                }

                var status = root.GetProperty("status").GetInt32();
                var body = root.GetProperty("body").GetString() ?? string.Empty;
                completion.TrySetResult(new RustHelpFetchResult(status, body));
            }
            catch (JsonException exception)
            {
                completion.TrySetException(new InvalidDataException(
                    "RustHelp returned an invalid browser response.",
                    exception));
            }
        }

        core.WebMessageReceived += OnWebMessageReceived;
        try
        {
            var idJson = JsonSerializer.Serialize(requestId);
            var pathJson = JsonSerializer.Serialize(path);
            var script = $$"""
                (async () => {
                    try {
                        const response = await fetch({{pathJson}}, { cache: "no-store" });
                        const body = await response.text();
                        window.chrome.webview.postMessage({
                            requestId: {{idJson}},
                            status: response.status,
                            body
                        });
                    } catch (error) {
                        window.chrome.webview.postMessage({
                            requestId: {{idJson}},
                            error: String(error)
                        });
                    }
                })();
                """;
            await core.ExecuteScriptAsync(script);
            var result = await completion.Task.WaitAsync(BrowserTimeout, cancellationToken);
            if (result.StatusCode is < 200 or >= 300)
            {
                throw new HttpRequestException(
                    $"RustHelp returned HTTP {result.StatusCode} for the item catalog.",
                    null,
                    (HttpStatusCode)result.StatusCode);
            }

            return result.Body;
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException("RustHelp did not return its item catalog in time.", exception);
        }
        finally
        {
            core.WebMessageReceived -= OnWebMessageReceived;
        }
    }

    private static RustHelpItemDetails BuildItemDetails(RustHelpItemPageSnapshot snapshot, string expectedSlug)
    {
        var shortName = snapshot.ShortName?.Trim() ?? string.Empty;
        var name = snapshot.Name?.Trim() ?? string.Empty;
        var pageUrl = snapshot.PageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)
            || string.IsNullOrWhiteSpace(shortName)
            || !string.Equals(snapshot.PageSlug, expectedSlug, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var parsedPageUrl)
            || !string.Equals(parsedPageUrl.Host, "rusthelp.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"RustHelp returned an invalid item page for '{expectedSlug}'.");
        }

        RustHelpCraftInfo? craft = null;
        if (snapshot.Recipe is { } recipe)
        {
            if (recipe.Ingredients.Count == 0
                || recipe.OutputAmount is not > 0
                || recipe.Ingredients.Any(ingredient =>
                    ingredient.Quantity <= 0
                    || string.IsNullOrWhiteSpace(ingredient.Name)
                    || !IsSafePageSlug(ingredient.PageSlug)))
            {
                throw new InvalidDataException(
                    $"RustHelp returned an incomplete crafting recipe for '{name}'.");
            }

            craft = new RustHelpCraftInfo
            {
                Ingredients = recipe.Ingredients
                    .Select(ingredient => new RustHelpIngredient
                    {
                        Name = ingredient.Name.Trim(),
                        PageSlug = ingredient.PageSlug,
                        Qty = ingredient.Quantity,
                    })
                    .ToList(),
                Amount = recipe.OutputAmount.Value,
                Workbench = Math.Max(0, recipe.WorkbenchLevel ?? 0),
                TimeSec = recipe.TimeSec is > 0 and <= int.MaxValue
                    ? recipe.TimeSec
                    : null,
            };
        }

        var blueprint = snapshot.ResearchScrap is not null
            || snapshot.TechTreeBlueprintPages is not null
            || snapshot.WorkbenchTier is not null
            ? new RustHelpBlueprintInfo
            {
                ResearchScrap = snapshot.ResearchScrap,
                TechTreeBlueprintPages = snapshot.TechTreeBlueprintPages,
                Workbench = snapshot.WorkbenchTier,
            }
            : null;

        return new RustHelpItemDetails
        {
            PageSlug = expectedSlug,
            PageUrl = parsedPageUrl.AbsoluteUri,
            ShortName = shortName,
            Name = name,
            Description = snapshot.Description?.Trim() ?? string.Empty,
            Id = int.TryParse(snapshot.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : null,
            VerifiedAt = snapshot.VerifiedAt?.Trim(),
            Craft = craft,
            Blueprint = blueprint,
            LootedFrom = snapshot.LootedFrom
                .Where(source => !string.IsNullOrWhiteSpace(source.Name)
                    && !string.IsNullOrWhiteSpace(source.Chance))
                .Select(source => new RustHelpLootSource
                {
                    Name = source.Name.Trim(),
                    Chance = source.Chance.Trim(),
                    Amount = source.Amount?.Trim() ?? string.Empty,
                    PageUrl = source.PageUrl?.Trim() ?? string.Empty,
                })
                .ToList(),
            Sections = snapshot.Sections
                .Where(section => !string.IsNullOrWhiteSpace(section.Name))
                .Select(section => new RustHelpItemSection
                {
                    Name = section.Name.Trim(),
                    Description = section.Description?.Trim() ?? string.Empty,
                    Rows = section.Rows
                        .Where(row => !string.IsNullOrWhiteSpace(row.Title)
                            || row.Cells.Any(cell => !string.IsNullOrWhiteSpace(cell.Text)
                                || cell.Entries.Count > 0))
                        .Select(row => new RustHelpSectionRow
                        {
                            Title = row.Title.Trim(),
                            IconUrl = GetSafeIconUrl(row.IconUrl),
                            Cells = row.Cells
                                .Select(cell => new RustHelpSectionCell
                                {
                                    Label = cell.Label.Trim(),
                                    Text = cell.Text.Trim(),
                                    Entries = cell.Entries
                                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                                        .Select(entry => new RustHelpSectionEntry
                                        {
                                            Name = entry.Name.Trim(),
                                            Quantity = entry.Quantity?.Trim() ?? string.Empty,
                                            IconUrl = GetSafeIconUrl(entry.IconUrl),
                                        })
                                        .ToList(),
                                })
                                .ToList(),
                        })
                        .ToList(),
                })
                .ToList(),
        };
    }

    private static string GetSafeIconUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (string.Equals(uri.Host, "cdn.rusthelp.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "rusthelp.com", StringComparison.OrdinalIgnoreCase))
                ? uri.AbsoluteUri
                : string.Empty;

    private static void ValidateCatalog(RustHelpCatalog? catalog)
    {
        if (catalog is null
            || catalog.Items.Count == 0
            || catalog.Items.Count > MaximumCatalogSize
            || catalog.FetchedAtUtc == default)
        {
            throw new InvalidDataException("The RustHelp item catalog is empty or invalid.");
        }

        var shortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.ShortName)
                || item.ShortName.Length > 128
                || item.ShortName.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.DisplayName.Length > 256
                || !shortNames.Add(item.ShortName)
                || !IsSafePageSlug(item.PageSlug)
                || !Uri.TryCreate(item.IconUrl, UriKind.Absolute, out var iconUri)
                || iconUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(iconUri.Host, "cdn.rusthelp.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("RustHelp returned an invalid or duplicate item entry.");
            }
        }
    }

    private static void ValidateSlug(string slug)
    {
        if (!IsSafePageSlug(slug))
        {
            throw new InvalidDataException("RustHelp returned an invalid item-page slug.");
        }
    }

    private static bool IsSafePageSlug(string? slug) =>
        !string.IsNullOrWhiteSpace(slug)
        && slug.Length <= 128
        && slug.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private sealed record RustHelpFetchResult(int StatusCode, string Body);

    private sealed class RustHelpItemPageSnapshot
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? PageSlug { get; set; }
        public string? PageUrl { get; set; }
        public string? ShortName { get; set; }
        public string? Id { get; set; }
        public string? VerifiedAt { get; set; }
        public int? WorkbenchTier { get; set; }
        public int? ResearchScrap { get; set; }
        public int? TechTreeBlueprintPages { get; set; }
        public RustHelpRecipeSnapshot? Recipe { get; set; }
        public List<RustHelpLootSourceSnapshot> LootedFrom { get; set; } = [];
        public List<RustHelpSectionSnapshot> Sections { get; set; } = [];
    }

    private sealed class RustHelpSectionSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<RustHelpSectionRowSnapshot> Rows { get; set; } = [];
    }

    private sealed class RustHelpSectionRowSnapshot
    {
        public string Title { get; set; } = string.Empty;
        public string? IconUrl { get; set; }
        public List<RustHelpSectionCellSnapshot> Cells { get; set; } = [];
    }

    private sealed class RustHelpSectionCellSnapshot
    {
        public string Label { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public List<RustHelpSectionEntrySnapshot> Entries { get; set; } = [];
    }

    private sealed class RustHelpSectionEntrySnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string? Quantity { get; set; }
        public string? IconUrl { get; set; }
    }

    private sealed class RustHelpRecipeSnapshot
    {
        public List<RustHelpIngredientSnapshot> Ingredients { get; set; } = [];
        public long? OutputAmount { get; set; }
        public int? WorkbenchLevel { get; set; }
        public int? TimeSec { get; set; }
    }

    private sealed class RustHelpIngredientSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string PageSlug { get; set; } = string.Empty;
        public long Quantity { get; set; }
    }

    private sealed class RustHelpLootSourceSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string Chance { get; set; } = string.Empty;
        public string? Amount { get; set; }
        public string? PageUrl { get; set; }
    }

    private const string ExtractItemPageScript = """
        (() => {
            const clean = value => (value ?? "").replace(/\s+/g, " ").trim();
            const text = node => clean(node?.innerText);
            const panels = Array.from(document.querySelectorAll('[role="tabpanel"]'));
            const metadata = label => {
                const heading = Array.from(document.querySelectorAll("h4"))
                    .find(node => text(node).toLowerCase() === label.toLowerCase());
                return text(heading?.parentElement?.querySelector("p"));
            };
            const parseQuantity = node => {
                const value = text(node?.querySelector("p")) || text(node);
                const match = value.match(/[×x]\s*([\d,]+)/i);
                return match ? Number(match[1].replace(/,/g, "")) : null;
            };
            const parseEntry = link => {
                const image = link.querySelector("img");
                const quantity = parseQuantity(link);
                return {
                    name: clean(image?.alt) || text(link),
                    quantity: quantity == null ? "" : `×${quantity.toLocaleString("en-US")}`,
                    iconUrl: image?.currentSrc || image?.src || ""
                };
            };
            const plainCell = cell => {
                const clone = cell.cloneNode(true);
                clone.querySelectorAll("a, img, button").forEach(node => node.remove());
                return text(clone);
            };
            const readSection = panel => {
                const tabId = panel.getAttribute("aria-labelledby");
                const name = (tabId ? text(document.getElementById(tabId)) : "")
                    || text(panel.querySelector("h2"));
                const rows = Array.from(panel.querySelectorAll("table")).flatMap(table => {
                    const headerCells = Array.from(table.querySelectorAll("thead th, tr:first-child th"));
                    const headers = headerCells.map(text);
                    if (name.toLowerCase() === "crafting"
                        && headers.some(header => header.toLowerCase() === "crafting recipe")) {
                        return [];
                    }

                    return Array.from(table.querySelectorAll("tbody tr")).map(row => {
                        const cells = Array.from(row.cells);
                        const firstEntries = Array.from(cells[0]?.querySelectorAll("a[href]") || [])
                            .map(parseEntry);
                        const title = firstEntries[0]?.name || plainCell(cells[0] || document.createElement("td"));
                        const iconUrl = firstEntries[0]?.iconUrl || "";
                        return {
                            title,
                            iconUrl,
                            cells: cells.map((cell, index) => {
                                const links = Array.from(cell.querySelectorAll("a[href]")).map(parseEntry);
                                const entries = index === 0 ? links.slice(1) : links;
                                return {
                                    label: headers[index] || (index === 0 ? "Items" : "Details"),
                                    text: plainCell(cell),
                                    entries
                                };
                            }).filter(cell => cell.text || cell.entries.length)
                        };
                    });
                });
                return {
                    name,
                    description: text(panel.querySelector("p")),
                    rows
                };
            };
            const getPageSlug = href => {
                try {
                    const url = new URL(href, location.href);
                    if (url.hostname !== "rusthelp.com" || !url.pathname.startsWith("/items/")) return "";
                    return decodeURIComponent(url.pathname.split("/").filter(Boolean).pop() || "");
                } catch {
                    return "";
                }
            };
            const craftPanel = panels.find(panel => text(panel.querySelector("h2")).toLowerCase() === "crafting");
            const craftTables = Array.from(craftPanel?.querySelectorAll("table") || []);
            const recipeTable = craftTables.find(table =>
                Array.from(table.querySelectorAll("th")).some(cell => text(cell).toLowerCase() === "crafting recipe"));
            const ingredients = [];
            let outputAmount = null;
            if (recipeTable) {
                for (const definition of Array.from(recipeTable.querySelectorAll("dd"))) {
                    const kind = text(definition.previousElementSibling).toLowerCase();
                    for (const link of Array.from(definition.querySelectorAll("a[href]"))) {
                        const imageName = clean(link.querySelector("img")?.alt);
                        const quantity = parseQuantity(link);
                        if (kind === "output") {
                            outputAmount = quantity;
                        } else if (kind === "input") {
                            ingredients.push({
                                name: imageName || text(link),
                                pageSlug: getPageSlug(link.getAttribute("href")),
                                quantity: quantity || 0
                            });
                        }
                    }
                }
            }

            const workbenchTable = craftTables.find(table => {
                const headers = Array.from(table.querySelectorAll("th")).map(text).join(" ").toLowerCase();
                return headers.includes("workbench") && headers.includes("craft time");
            });
            const workbenchRow = workbenchTable?.querySelector("tbody tr");
            const workbenchText = text(workbenchRow?.cells?.[0]);
            const timeText = text(workbenchRow?.cells?.[1]);
            const timeParts = Array.from(timeText.matchAll(/([\d.]+)\s*(h|m|s)\b/gi));
            const timeSec = timeParts.length
                ? Math.ceil(timeParts.reduce((total, part) => {
                    const value = Number(part[1]);
                    const unit = part[2].toLowerCase();
                    return total + value * (unit === "h" ? 3600 : unit === "m" ? 60 : 1);
                }, 0))
                : null;
            const tierValue = metadata("Workbench Tier") || workbenchText;
            const tierMatch = tierValue.match(/(?:level|tier)\s*(\d+)/i);
            const workbenchLevel = tierMatch ? Number(tierMatch[1]) : 0;
            const researchTable = craftTables.find(table =>
                Array.from(table.querySelectorAll("th")).some(cell => text(cell).toLowerCase() === "research table cost"));
            const techTreeTable = craftTables.find(table =>
                Array.from(table.querySelectorAll("th")).some(cell => text(cell).toLowerCase() === "tech tree cost"));
            const researchScrap = researchTable
                ? Math.max(0, ...Array.from(researchTable.querySelectorAll("a[href]"))
                    .filter(link => clean(link.querySelector("img")?.alt).toLowerCase() === "scrap")
                    .map(link => parseQuantity(link) || 0))
                : null;
            const techTreeBlueprintPages = techTreeTable
                ? Math.max(0, ...Array.from(techTreeTable.querySelectorAll("a[href]"))
                    .filter(link => /blueprint page/i.test(clean(link.querySelector("img")?.alt)))
                    .map(link => parseQuantity(link) || 0))
                : null;
            const lootPanel = panels.find(panel => text(panel.querySelector("h2")).toLowerCase() === "looted from");
            const lootTable = lootPanel?.querySelector("table");
            const lootRows = Array.from(lootTable?.querySelectorAll("tbody tr") || []);
            const lootSources = lootRows.map(row => {
                const cells = Array.from(row.cells);
                const link = cells[0]?.querySelector("a[href]");
                return {
                    name: clean(link?.querySelector("img")?.alt) || text(cells[0]),
                    chance: text(cells[1]),
                    amount: text(cells[2]),
                    pageUrl: link?.href || ""
                };
            }).filter(source => source.name && source.chance);
            const canonical = document.querySelector('link[rel="canonical"]')?.href || location.href.split("#")[0];
            const pageSlug = getPageSlug(canonical);
            const sections = panels.map(readSection).filter(section => section.name);
            const recipe = recipeTable
                ? { ingredients, outputAmount, workbenchLevel, timeSec }
                : null;
            return JSON.stringify({
                name: text(document.querySelector("h1")),
                description: text(document.querySelector("h1")?.parentElement?.querySelector("p")),
                pageSlug,
                pageUrl: canonical,
                shortName: metadata("Short name"),
                id: metadata("ID"),
                verifiedAt: metadata("Data last verified"),
                workbenchTier: tierMatch ? workbenchLevel : null,
                researchScrap,
                techTreeBlueprintPages,
                recipe,
                lootedFrom: lootSources,
                sections
            });
        })()
        """;
}
