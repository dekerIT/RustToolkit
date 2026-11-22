namespace RustToolkit.Desktop.Models;

public sealed class CraftingItem
{
    public string ShortName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public long AmountToCreate { get; set; }
    public int WorkbenchLevelRequired { get; set; }
    public List<CraftingIngredient> Ingredients { get; set; } = [];
}

public sealed class CraftingIngredient
{
    public string ShortName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long Amount { get; set; }
}

public sealed class CraftingCatalog
{
    public DateTimeOffset FetchedAtUtc { get; set; }
    public DateTimeOffset? SourceUpdatedAtUtc { get; set; }
    public List<CraftingItem> Items { get; set; } = [];
}

public sealed record CraftingStep(
    string ShortName,
    string DisplayName,
    long Batches,
    long ProducedAmount,
    int WorkbenchLevelRequired,
    int Depth);

public sealed record CraftingMaterial(string ShortName, string DisplayName, long Amount);

public sealed class CraftingCalculation
{
    public List<CraftingStep> Steps { get; } = [];
    public Dictionary<string, CraftingMaterial> Materials { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int WorkbenchLevelRequired { get; set; }
    public long RequestedAmount { get; set; }
    public long ProducedAmount { get; set; }
}
