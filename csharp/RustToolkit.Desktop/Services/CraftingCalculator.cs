using System.IO;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class CraftingCalculator
{
    private const int MaximumRecipeDepth = 24;

    public CraftingCalculation Calculate(
        IReadOnlyDictionary<string, CraftingItem> catalog,
        string shortName,
        long requestedAmount)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortName);
        if (requestedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedAmount), "The requested amount must be positive.");
        }

        if (!catalog.TryGetValue(shortName, out var item))
        {
            throw new InvalidOperationException($"No crafting recipe was found for '{shortName}'.");
        }

        var result = new CraftingCalculation { RequestedAmount = requestedAmount };
        ExpandRecipe(catalog, item, requestedAmount, 0, [], result);
        result.ProducedAmount = result.Steps[0].ProducedAmount;
        return result;
    }

    private static void ExpandRecipe(
        IReadOnlyDictionary<string, CraftingItem> catalog,
        CraftingItem item,
        long requestedAmount,
        int depth,
        HashSet<string> recipePath,
        CraftingCalculation result)
    {
        if (depth > MaximumRecipeDepth)
        {
            throw new InvalidDataException("The recipe tree is unexpectedly deep and cannot be calculated.");
        }

        if (item.AmountToCreate <= 0)
        {
            throw new InvalidDataException($"The recipe for {item.DisplayName} has an invalid output quantity.");
        }

        if (!recipePath.Add(item.ShortName))
        {
            throw new InvalidDataException($"The recipe data contains a circular dependency at {item.DisplayName}.");
        }

        try
        {
            var batches = DivideRoundUp(requestedAmount, item.AmountToCreate);
            var producedAmount = checked(batches * item.AmountToCreate);
            result.Steps.Add(new CraftingStep(
                item.ShortName,
                item.DisplayName,
                batches,
                producedAmount,
                item.WorkbenchLevelRequired,
                depth));
            result.WorkbenchLevelRequired = Math.Max(result.WorkbenchLevelRequired, item.WorkbenchLevelRequired);

            foreach (var ingredient in item.Ingredients)
            {
                if (ingredient.Amount <= 0 || string.IsNullOrWhiteSpace(ingredient.ShortName))
                {
                    throw new InvalidDataException($"The recipe for {item.DisplayName} has an invalid ingredient.");
                }

                var requiredAmount = checked(ingredient.Amount * batches);
                if (catalog.TryGetValue(ingredient.ShortName, out var ingredientRecipe))
                {
                    ExpandRecipe(catalog, ingredientRecipe, requiredAmount, depth + 1, recipePath, result);
                    continue;
                }

                if (result.Materials.TryGetValue(ingredient.ShortName, out var existing))
                {
                    result.Materials[ingredient.ShortName] = existing with
                    {
                        Amount = checked(existing.Amount + requiredAmount),
                    };
                }
                else
                {
                    result.Materials.Add(
                        ingredient.ShortName,
                        new CraftingMaterial(
                            ingredient.ShortName,
                            ingredient.DisplayName,
                            requiredAmount));
                }
            }
        }
        finally
        {
            recipePath.Remove(item.ShortName);
        }
    }

    private static long DivideRoundUp(long value, long divisor) =>
        value / divisor + (value % divisor == 0 ? 0 : 1);
}
