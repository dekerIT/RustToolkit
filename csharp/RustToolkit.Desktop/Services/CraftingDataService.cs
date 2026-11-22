using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class CraftingDataService : IDisposable
{
    private const string ApiBaseUrl = "https://rust-api.tafu.casa/";
    private const int PageSize = 250;
    private const int MaximumCatalogSize = 10_000;
    private const long MaximumIconSizeBytes = 2 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _cachePath;
    private readonly string _iconCacheDirectory;

    public CraftingDataService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RustToolkit/1.0");

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustToolkit",
            "crafting");
        _cachePath = Path.Combine(dataDirectory, "catalog.json");
        _iconCacheDirectory = Path.Combine(dataDirectory, "icons");
    }

    public async Task<CraftingCatalog?> LoadCachedCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cachePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_cachePath);
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
                $"The saved crafting catalog is invalid and was not changed: {_cachePath}",
                exception);
        }
    }

    public async Task<CraftingCatalog> DownloadLatestCatalogAsync(CancellationToken cancellationToken = default)
    {
        var health = await _httpClient.GetFromJsonAsync<ApiHealth>("health", JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The crafting data source returned an empty health response.");
        if (!health.Ready)
        {
            throw new HttpRequestException($"The crafting data source is not ready: {health.Message}");
        }

        var items = new List<CraftingItem>();
        var offset = 0;
        int? total = null;
        do
        {
            var response = await _httpClient.GetFromJsonAsync<ApiCraftingResponse>(
                $"api/crafting?limit={PageSize}&offset={offset}",
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidDataException("The crafting data source returned an empty recipe page.");

            total ??= response.Total;
            if (total is < 0 or > MaximumCatalogSize || response.Items is null)
            {
                throw new InvalidDataException("The crafting data source returned invalid catalog metadata.");
            }

            if (response.Items.Count == 0 && items.Count < total)
            {
                throw new InvalidDataException("The crafting data source returned an incomplete recipe catalog.");
            }

            items.AddRange(response.Items.Select(MapItem));
            offset += response.Items.Count;
        }
        while (items.Count < total);

        var catalog = new CraftingCatalog
        {
            FetchedAtUtc = DateTimeOffset.UtcNow,
            SourceUpdatedAtUtc = health.LastUpdate,
            Items = items,
        };
        ValidateCatalog(catalog);
        return catalog;
    }

    public async Task SaveCatalogAsync(CraftingCatalog catalog, CancellationToken cancellationToken = default)
    {
        ValidateCatalog(catalog);
        var directory = Path.GetDirectoryName(_cachePath)
            ?? throw new IOException("Could not determine the crafting cache directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_cachePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, catalog, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _cachePath, overwrite: true);
    }

    public async Task<byte[]?> LoadIconAsync(string shortName, CancellationToken cancellationToken = default)
    {
        ValidateShortName(shortName);
        var cachePath = Path.Combine(_iconCacheDirectory, $"{shortName}.png");
        if (File.Exists(cachePath))
        {
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }

        using var response = await _httpClient.GetAsync(
            $"api/items/{Uri.EscapeDataString(shortName)}/image",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumIconSizeBytes)
        {
            throw new InvalidDataException($"The item icon for '{shortName}' exceeds the allowed size.");
        }

        var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (imageBytes.Length == 0
            || imageBytes.Length > MaximumIconSizeBytes
            || !imageBytes.AsSpan().StartsWith(PngSignature))
        {
            throw new InvalidDataException($"The image source returned an invalid PNG for '{shortName}'.");
        }

        Directory.CreateDirectory(_iconCacheDirectory);
        var temporaryPath = $"{cachePath}.tmp";
        await File.WriteAllBytesAsync(temporaryPath, imageBytes, cancellationToken);
        File.Move(temporaryPath, cachePath, overwrite: true);
        return imageBytes;
    }

    public void Dispose() => _httpClient.Dispose();

    private static CraftingItem MapItem(ApiCraftingItem item)
    {
        ValidateShortName(item.ShortName);
        if (string.IsNullOrWhiteSpace(item.DisplayName)
            || item.AmountToCreate is null or <= 0
            || item.WorkbenchLevelRequired is null or < 0
            || item.Ingredients is null)
        {
            throw new InvalidDataException($"The crafting data for '{item.ShortName}' is incomplete.");
        }

        var ingredients = new List<CraftingIngredient>();
        foreach (var ingredient in item.Ingredients)
        {
            if (ingredient.Amount is null or <= 0
                || ingredient.ItemDef is null
                || string.IsNullOrWhiteSpace(ingredient.ItemDef.DisplayName)
                || string.IsNullOrWhiteSpace(ingredient.ItemDef.ShortName))
            {
                throw new InvalidDataException($"The crafting data for '{item.ShortName}' has an invalid ingredient.");
            }

            ValidateShortName(ingredient.ItemDef.ShortName);
            ingredients.Add(new CraftingIngredient
            {
                ShortName = ingredient.ItemDef.ShortName,
                DisplayName = ingredient.ItemDef.DisplayName,
                Amount = ingredient.Amount.Value,
            });
        }

        return new CraftingItem
        {
            ShortName = item.ShortName,
            DisplayName = item.DisplayName,
            CategoryName = item.CategoryName ?? string.Empty,
            AmountToCreate = item.AmountToCreate.Value,
            WorkbenchLevelRequired = item.WorkbenchLevelRequired.Value,
            Ingredients = ingredients,
        };
    }

    private static void ValidateCatalog(CraftingCatalog? catalog)
    {
        if (catalog is null || catalog.Items.Count == 0 || catalog.Items.Count > MaximumCatalogSize)
        {
            throw new InvalidDataException("The crafting catalog is empty or exceeds the supported size.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            ValidateShortName(item.ShortName);
            if (string.IsNullOrWhiteSpace(item.DisplayName)
                || item.AmountToCreate <= 0
                || item.WorkbenchLevelRequired < 0
                || item.Ingredients is null
                || !names.Add(item.ShortName))
            {
                throw new InvalidDataException("The crafting catalog contains an invalid or duplicate recipe.");
            }

            foreach (var ingredient in item.Ingredients)
            {
                ValidateShortName(ingredient.ShortName);
                if (string.IsNullOrWhiteSpace(ingredient.DisplayName) || ingredient.Amount <= 0)
                {
                    throw new InvalidDataException($"The recipe for {item.DisplayName} has invalid ingredient data.");
                }
            }
        }
    }

    private static void ValidateShortName(string shortName)
    {
        if (string.IsNullOrWhiteSpace(shortName)
            || shortName.Length > 128
            || shortName.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                    && character is not '.' and not '_' and not '-' and not ' '))
        {
            throw new InvalidDataException("The item data source returned an invalid item shortname.");
        }
    }

    private sealed class ApiHealth
    {
        [JsonPropertyName("ready")]
        public bool Ready { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("lastUpdate")]
        public DateTimeOffset? LastUpdate { get; set; }
    }

    private sealed class ApiCraftingResponse
    {
        [JsonPropertyName("items")]
        public List<ApiCraftingItem>? Items { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class ApiCraftingItem
    {
        [JsonPropertyName("shortname")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("categoryName")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("amountToCreate")]
        public long? AmountToCreate { get; set; }

        [JsonPropertyName("workbenchLevelRequired")]
        public int? WorkbenchLevelRequired { get; set; }

        [JsonPropertyName("ingredients")]
        public List<ApiIngredient>? Ingredients { get; set; }
    }

    private sealed class ApiIngredient
    {
        [JsonPropertyName("amount")]
        public long? Amount { get; set; }

        [JsonPropertyName("itemDef")]
        public ApiIngredientItem? ItemDef { get; set; }
    }

    private sealed class ApiIngredientItem
    {
        [JsonPropertyName("shortname")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }
}
