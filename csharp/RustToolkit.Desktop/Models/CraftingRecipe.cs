namespace RustToolkit.Desktop.Models;

public sealed class CraftingCatalog
{
    public DateTimeOffset FetchedAtUtc { get; set; }
    public RustlyApiMetadata Metadata { get; set; } = new();
    public List<RustlyItemSummary> Items { get; set; } = [];
}

public sealed class RustlyApiMetadata
{
    public string Version { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? VerifiedAt { get; set; }
    public string? GameVersion { get; set; }
    public string? ChangedAt { get; set; }
    public int? Total { get; set; }
}

public sealed class RustlyItemSummary
{
    public string Slug { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Craftable { get; set; }
    public int? ItemId { get; set; }
}

public sealed class RustlyItemDetails
{
    public string Slug { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RustlyCraftInfo? Craft { get; set; }
    public RustlyBlueprintInfo? Blueprint { get; set; }
    public List<string> LootedFrom { get; set; } = [];
}

public sealed class RustlyCraftInfo
{
    public List<RustlyIngredient> Ingredients { get; set; } = [];
    public List<RustlyIngredient>? TotalIngredients { get; set; }
    public long? Amount { get; set; }
    public int? Workbench { get; set; }
    public int? TimeSec { get; set; }
}

public sealed class RustlyIngredient
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public long Qty { get; set; }
}

public sealed class RustlyBlueprintInfo
{
    public int? ResearchScrap { get; set; }
    public int? TechTreeScrap { get; set; }
    public int? TechTreeTotalScrap { get; set; }
    public int? Workbench { get; set; }
}

public sealed record RustlyItemDetailsResult(
    RustlyItemDetails Item,
    RustlyApiMetadata Metadata,
    DateTimeOffset FetchedAtUtc,
    bool IsCached);

public sealed class RustlyRaidTarget
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int Hp { get; set; }
    public bool HasSoftSide { get; set; }
}

public sealed record RustlyRaidTargetCatalog
{
    public List<RustlyRaidTarget> Targets { get; set; } = [];
    public RustlyApiMetadata Metadata { get; set; } = new();
    public DateTimeOffset FetchedAtUtc { get; set; }
    public bool IsCached { get; set; }
}

public sealed class RustlyRaidPlan
{
    public RustlyRaidPlanTarget Target { get; set; } = new();
    public string Side { get; set; } = string.Empty;
    public List<RustlyExplosiveMethod> Explosives { get; set; } = [];
}

public sealed class RustlyRaidPlanTarget
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Hp { get; set; }
}

public sealed class RustlyExplosiveMethod
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long Sulfur { get; set; }
    public long Charcoal { get; set; }
    public long MetalFragments { get; set; }
    public long Seconds { get; set; }
    public bool Cheapest { get; set; }
}

public sealed record RustlyRaidPlanResult(
    RustlyRaidPlan Plan,
    RustlyApiMetadata Metadata,
    DateTimeOffset FetchedAtUtc,
    bool IsCached);

public sealed record RustlyRaidEstimate(
    RustlyExplosiveMethod Method,
    long RequiredAmount,
    long UsedFromInventory,
    long BringAmount,
    long Sulfur,
    long Charcoal,
    long MetalFragments,
    long Seconds,
    long EstimatedRemainingHp);

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
