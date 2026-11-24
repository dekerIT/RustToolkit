using System.Text.Json.Serialization;

namespace RustToolkit.Desktop.Models;

public sealed class RustHelpCatalog
{
    public DateTimeOffset FetchedAtUtc { get; set; }
    public List<RustHelpItemSummary> Items { get; set; } = [];
}

public sealed class RustHelpItemSummary
{
    public string ShortName { get; set; } = string.Empty;
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string PageSlug => RustHelpSlug.Create(DisplayName);
}

public sealed class RustHelpItemDetails
{
    public string PageSlug { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Id { get; set; }
    public string? VerifiedAt { get; set; }
    public RustHelpCraftInfo? Craft { get; set; }
    public RustHelpBlueprintInfo? Blueprint { get; set; }
    public List<RustHelpLootSource> LootedFrom { get; set; } = [];
    public List<RustHelpItemSection> Sections { get; set; } = [];
}

public sealed class RustHelpItemSection
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<RustHelpSectionRow> Rows { get; set; } = [];
}

public sealed class RustHelpSectionRow
{
    public string Title { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public List<RustHelpSectionCell> Cells { get; set; } = [];
    public string Initials => CreateInitials(Title);

    private static string CreateInitials(string value) =>
        new(value.Where(char.IsLetterOrDigit).Take(2).Select(char.ToUpperInvariant).ToArray());
}

public sealed class RustHelpSectionCell
{
    public string Label { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<RustHelpSectionEntry> Entries { get; set; } = [];
}

public sealed class RustHelpSectionEntry
{
    public string Name { get; set; } = string.Empty;
    public string Quantity { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public string Initials =>
        new(Name.Where(char.IsLetterOrDigit).Take(2).Select(char.ToUpperInvariant).ToArray());
}

public sealed class RustHelpCraftInfo
{
    public List<RustHelpIngredient> Ingredients { get; set; } = [];
    public long Amount { get; set; } = 1;
    public int Workbench { get; set; }
    public int? TimeSec { get; set; }
}

public sealed class RustHelpBlueprintInfo
{
    public int? ResearchScrap { get; set; }
    public int? TechTreeBlueprintPages { get; set; }
    public int? Workbench { get; set; }
}

public sealed class RustHelpIngredient
{
    public string Name { get; set; } = string.Empty;
    public string PageSlug { get; set; } = string.Empty;
    public long Qty { get; set; }
}

public sealed class RustHelpLootSource
{
    public string Name { get; set; } = string.Empty;
    public string Chance { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
}

public sealed record RustHelpItemDetailsResult(
    RustHelpItemDetails Item,
    DateTimeOffset FetchedAtUtc,
    bool IsCached);

internal static class RustHelpSlug
{
    public static string Create(string name)
    {
        var normalized = name.Normalize(System.Text.NormalizationForm.FormD);
        var result = new System.Text.StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var character in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && result.Length > 0)
                {
                    result.Append('-');
                }

                result.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else if (character != '.')
            {
                pendingSeparator = true;
            }
        }

        return result.ToString();
    }
}
