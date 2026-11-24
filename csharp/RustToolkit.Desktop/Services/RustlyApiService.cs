using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class RustlyApiService : IDisposable
{
    private const string ApiBaseUrl = "https://rustly.com/api/v1/";
    private const int PageSize = 200;
    private const int MaximumCatalogSize = 5_000;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _catalogCachePath;
    private readonly Dictionary<string, RustlyItemDetailsResult> _itemDetails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RustlyRaidPlanResult> _raidPlans = new(StringComparer.OrdinalIgnoreCase);
    private RustlyRaidTargetCatalog? _raidTargetCache;

    public RustlyApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(20),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RustToolkit/1.0");

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustToolkit",
            "rustly");
        _catalogCachePath = Path.Combine(dataDirectory, "items.json");
    }

    public async Task<CraftingCatalog?> LoadCachedCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_catalogCachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_catalogCachePath);
            var catalog = await JsonSerializer.DeserializeAsync<CraftingCatalog>(
                stream,
                JsonOptions,
                cancellationToken);
            ValidateCatalog(catalog);
            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The saved Rustly item catalog is invalid and was not changed: {_catalogCachePath}",
                exception);
        }
    }

    public async Task<CraftingCatalog> DownloadLatestCatalogAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<RustlyItemSummary>();
        RustlyApiMetadata? metadata = null;
        var offset = 0;
        int? total = null;

        do
        {
            var page = await GetApiResponseAsync<List<RustlyItemSummary>>(
                $"items/?limit={PageSize}&offset={offset}",
                cancellationToken);
            metadata ??= page.Metadata;
            total ??= page.Metadata.Total;

            if (total is null or < 0 or > MaximumCatalogSize)
            {
                throw new InvalidDataException("Rustly returned an invalid item catalog size.");
            }

            if (page.Data.Count == 0 && items.Count < total.Value)
            {
                throw new InvalidDataException("Rustly returned an incomplete item catalog.");
            }

            foreach (var item in page.Data)
            {
                ValidateItemSummary(item);
            }

            items.AddRange(page.Data);
            offset += page.Data.Count;
        }
        while (items.Count < total);

        var catalog = new CraftingCatalog
        {
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Metadata = metadata ?? throw new InvalidDataException("Rustly returned no item catalog metadata."),
            Items = items,
        };
        ValidateCatalog(catalog);
        return catalog;
    }

    public async Task SaveCatalogAsync(CraftingCatalog catalog, CancellationToken cancellationToken = default)
    {
        ValidateCatalog(catalog);
        var directory = Path.GetDirectoryName(_catalogCachePath)
            ?? throw new IOException("Could not determine the Rustly cache directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_catalogCachePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _catalogCachePath, overwrite: true);
    }

    public async Task<RustlyItemDetailsResult> GetItemDetailsAsync(
        string slug,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(slug);
        if (!forceRefresh
            && _itemDetails.TryGetValue(slug, out var cached)
            && DateTimeOffset.UtcNow - cached.FetchedAtUtc < CacheLifetime)
        {
            return cached with { IsCached = true };
        }

        var response = await GetApiResponseAsync<RustlyItemDetails>(
            $"items/{Uri.EscapeDataString(slug)}/",
            cancellationToken);
        ValidateItemDetails(response.Data, slug);
        var result = new RustlyItemDetailsResult(
            response.Data,
            response.Metadata,
            DateTimeOffset.UtcNow,
            IsCached: false);
        _itemDetails[slug] = result;
        return result;
    }

    public async Task<RustlyRaidTargetCatalog> GetRaidTargetsAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh
            && _raidTargetCache is not null
            && _raidTargetCache.Targets.Count > 0
            && DateTimeOffset.UtcNow - _raidTargetCache.FetchedAtUtc < CacheLifetime)
        {
            return _raidTargetCache with { IsCached = true };
        }

        var response = await GetApiResponseAsync<List<RustlyRaidTarget>>("raid/", cancellationToken);
        if (response.Data.Count == 0 || response.Data.Count > MaximumCatalogSize)
        {
            throw new InvalidDataException("Rustly returned an empty or oversized raid target list.");
        }

        foreach (var target in response.Data)
        {
            ValidateSlug(target.Slug);
            if (string.IsNullOrWhiteSpace(target.Name) || target.Hp <= 0)
            {
                throw new InvalidDataException("Rustly returned an invalid raid target.");
            }
        }

        _raidTargetCache = new RustlyRaidTargetCatalog
        {
            Targets = response.Data,
            Metadata = response.Metadata,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        };
        return _raidTargetCache;
    }

    public async Task<RustlyRaidPlanResult> GetRaidPlanAsync(
        string targetSlug,
        bool softSide,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ValidateSlug(targetSlug);
        var cacheKey = $"{targetSlug}:{softSide}";
        if (!forceRefresh
            && _raidPlans.TryGetValue(cacheKey, out var cached)
            && DateTimeOffset.UtcNow - cached.FetchedAtUtc < CacheLifetime)
        {
            return cached with { IsCached = true };
        }

        var soft = softSide ? 1 : 0;
        var response = await GetApiResponseAsync<RustlyRaidPlan>(
            $"raid/{Uri.EscapeDataString(targetSlug)}/?qty=1&soft={soft}",
            cancellationToken);
        ValidateRaidPlan(response.Data, targetSlug, softSide);
        var result = new RustlyRaidPlanResult(
            response.Data,
            response.Metadata,
            DateTimeOffset.UtcNow,
            IsCached: false);
        _raidPlans[cacheKey] = result;
        return result;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<RustlyApiResponse<T>> GetApiResponseAsync<T>(
        string relativeUri,
        CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _httpClient.GetAsync(
            relativeUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var detail = ReadApiError(body);
            throw new HttpRequestException(
                $"Rustly API returned {(int)response.StatusCode} ({response.StatusCode}): {detail}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<RustlyApiWireResponse<T>>(
            JsonOptions,
            cancellationToken);
        if (payload?.Data is not T data || payload.Metadata is not { } metadata)
        {
            throw new InvalidDataException($"Rustly returned an invalid response for '{relativeUri}'.");
        }

        return new RustlyApiResponse<T>(data, metadata);
    }

    private static string ReadApiError(string body)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var error = JsonSerializer.Deserialize<RustlyApiErrorResponse>(body, JsonOptions);
                if (!string.IsNullOrWhiteSpace(error?.Error?.Message))
                {
                    return error.Error.Message;
                }
            }
            catch (JsonException)
            {
            }

            return body.Length <= 300 ? body : body[..300];
        }

        return "The response body was empty.";
    }

    private static void ValidateCatalog(CraftingCatalog? catalog)
    {
        if (catalog is null
            || catalog.Items.Count == 0
            || catalog.Items.Count > MaximumCatalogSize
            || catalog.Metadata is null)
        {
            throw new InvalidDataException("The saved Rustly item catalog is empty or invalid.");
        }

        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            ValidateItemSummary(item);
            if (!slugs.Add(item.Slug))
            {
                throw new InvalidDataException("Rustly returned duplicate item slugs.");
            }
        }
    }

    private static void ValidateItemSummary(RustlyItemSummary item)
    {
        if (item is null)
        {
            throw new InvalidDataException("Rustly returned a null item summary.");
        }

        ValidateSlug(item.Slug);
        var shortName = item.ShortName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(item.Name)
            || shortName.Length > 128
            || shortName.Any(char.IsControl))
        {
            throw new InvalidDataException("Rustly returned an invalid item summary.");
        }

        item.ShortName = shortName;
        item.Category ??= string.Empty;
    }

    private static void ValidateItemDetails(RustlyItemDetails item, string expectedSlug)
    {
        if (item is null)
        {
            throw new InvalidDataException($"Rustly returned null details for '{expectedSlug}'.");
        }

        ValidateSlug(item.Slug);
        var shortName = item.ShortName ?? string.Empty;
        if (!string.Equals(item.Slug, expectedSlug, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(item.Name)
            || shortName.Length > 128
            || shortName.Any(char.IsControl))
        {
            throw new InvalidDataException($"Rustly returned invalid details for '{expectedSlug}'.");
        }

        item.ShortName = shortName;
        item.Category ??= string.Empty;
        if (item.Craft is not null)
        {
            if (item.Craft.Amount is <= 0
                || item.Craft.Workbench is < 0
                || item.Craft.TimeSec is < 0)
            {
                throw new InvalidDataException($"Rustly returned invalid recipe metadata for '{item.Name}'.");
            }

            ValidateIngredients(item.Craft.Ingredients, item.Name);
            if (item.Craft.TotalIngredients is not null)
            {
                ValidateIngredients(item.Craft.TotalIngredients, item.Name);
            }
        }

        item.LootedFrom ??= [];
    }

    private static void ValidateIngredients(IReadOnlyCollection<RustlyIngredient> ingredients, string itemName)
    {
        foreach (var ingredient in ingredients)
        {
            ValidateSlug(ingredient.Slug);
            if (string.IsNullOrWhiteSpace(ingredient.Name) || ingredient.Qty <= 0)
            {
                throw new InvalidDataException($"Rustly returned an invalid ingredient in the recipe for '{itemName}'.");
            }
        }
    }

    private static void ValidateRaidPlan(RustlyRaidPlan plan, string expectedSlug, bool softSide)
    {
        ValidateSlug(plan.Target.Slug);
        if (!string.Equals(plan.Target.Slug, expectedSlug, StringComparison.OrdinalIgnoreCase)
            || plan.Target.Hp <= 0
            || string.IsNullOrWhiteSpace(plan.Target.Name)
            || !string.Equals(plan.Side, softSide ? "soft" : "hard", StringComparison.OrdinalIgnoreCase)
            || plan.Explosives.Count == 0)
        {
            throw new InvalidDataException($"Rustly returned an invalid raid plan for '{expectedSlug}'.");
        }

        foreach (var method in plan.Explosives)
        {
            ValidateSlug(method.Slug);
            if (string.IsNullOrWhiteSpace(method.Name)
                || method.Amount < 0
                || method.Sulfur < 0
                || method.Charcoal < 0
                || method.MetalFragments < 0
                || method.Seconds < 0)
            {
                throw new InvalidDataException($"Rustly returned an invalid raid method for '{plan.Target.Name}'.");
            }
        }
    }

    private static void ValidateSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)
            || slug.Length > 128
            || slug.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidDataException("Rustly returned an invalid item or target slug.");
        }
    }

    private sealed record RustlyApiResponse<T>(T Data, RustlyApiMetadata Metadata)
        where T : class;

    private sealed class RustlyApiWireResponse<T>
        where T : class
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("meta")]
        public RustlyApiMetadata? Metadata { get; set; }
    }

    private sealed class RustlyApiErrorResponse
    {
        [JsonPropertyName("error")]
        public RustlyApiError? Error { get; set; }
    }

    private sealed class RustlyApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
