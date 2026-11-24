using System.IO;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public static class RaidEstimateCalculator
{
    public static IReadOnlyList<RustlyRaidEstimate> Calculate(
        RustlyRaidPlan plan,
        int currentHealth,
        int targetCount,
        IReadOnlyDictionary<string, long> ownedCounts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(ownedCounts);
        if (currentHealth is < 1 || currentHealth > plan.Target.Hp)
        {
            throw new ArgumentOutOfRangeException(nameof(currentHealth));
        }

        if (targetCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCount));
        }

        var totalHealth = checked((long)currentHealth * targetCount);
        var estimates = new List<RustlyRaidEstimate>();
        foreach (var method in plan.Explosives.Where(method => method.Amount > 0))
        {
            var perTargetAmount = DivideRoundUp(
                checked((decimal)currentHealth * method.Amount),
                plan.Target.Hp);
            var requiredAmount = checked(perTargetAmount * targetCount);
            var ownedAmount = ownedCounts.TryGetValue(method.Slug, out var count) ? count : 0;
            if (ownedAmount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownedCounts), "Owned explosive counts cannot be negative.");
            }

            var usedFromInventory = Math.Min(ownedAmount, requiredAmount);
            var bringAmount = requiredAmount - usedFromInventory;
            var estimatedRemainingHealth = Math.Max(
                0,
                (decimal)totalHealth - (decimal)usedFromInventory * plan.Target.Hp / method.Amount);

            estimates.Add(new RustlyRaidEstimate(
                method,
                requiredAmount,
                usedFromInventory,
                bringAmount,
                Scale(method.Sulfur, bringAmount, method.Amount),
                Scale(method.Charcoal, bringAmount, method.Amount),
                Scale(method.MetalFragments, bringAmount, method.Amount),
                Scale(method.Seconds, bringAmount, method.Amount),
                decimal.ToInt64(decimal.Ceiling(estimatedRemainingHealth))));
        }

        if (estimates.Count == 0)
        {
            throw new InvalidDataException($"Rustly provided no usable explosive methods for {plan.Target.Name}.");
        }

        return estimates;
    }

    private static long DivideRoundUp(decimal numerator, int denominator) =>
        decimal.ToInt64(decimal.Ceiling(numerator / denominator));

    private static long Scale(long baselineTotal, long newAmount, long baselineAmount) =>
        decimal.ToInt64(decimal.Ceiling((decimal)baselineTotal * newAmount / baselineAmount));
}
