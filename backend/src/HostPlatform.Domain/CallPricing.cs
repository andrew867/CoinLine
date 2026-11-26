namespace HostPlatform.Domain;

/// <summary>The first charge and recurring charge are separate from a duration-based estimate.</summary>
public sealed record CallPricing(CallPricingMode Mode, decimal InitialChargeUsd, decimal RatePerMinuteUsd)
{
    public static string? ValidationIssue(CallPricingMode mode, decimal initial, decimal additional)
    {
        if (!Enum.IsDefined(mode))
            return "Choose a supported pricing structure.";
        if (mode == CallPricingMode.LegacyProrated)
            return initial != 0 || additional < 0 ? "Legacy per-minute pricing requires a nonnegative rate and no separate initial charge." : null;
        if (!WholeCents(initial) || !WholeCents(additional))
            return "Charges must be nonnegative whole cents no greater than $655.35.";
        if (mode == CallPricingMode.FlatCall && additional != 0)
            return "A whole-call charge cannot also have a per-minute charge.";
        if (mode == CallPricingMode.PerStartedMinute && initial != 0)
            return "The same charge applies to the first and each following minute. Leave the separate initial charge zero.";
        return null;
    }

    private static bool WholeCents(decimal amount) => amount >= 0m && amount <= 655.35m && amount * 100m == decimal.Truncate(amount * 100m);

    public decimal FirstChargeUsd => Mode is CallPricingMode.FlatCall or CallPricingMode.FirstAndFollowingMinutes ? InitialChargeUsd : RatePerMinuteUsd;
    public decimal FollowingChargeUsd => Mode == CallPricingMode.FlatCall ? 0 : RatePerMinuteUsd;

    public decimal Estimate(decimal durationMinutes)
    {
        var minutes = Math.Max(0.01m, durationMinutes);
        return Mode switch
        {
            CallPricingMode.FlatCall => InitialChargeUsd,
            CallPricingMode.PerStartedMinute => Math.Ceiling(minutes) * RatePerMinuteUsd,
            CallPricingMode.FirstAndFollowingMinutes => InitialChargeUsd + (Math.Ceiling(minutes) - 1) * RatePerMinuteUsd,
            _ => Math.Round(RatePerMinuteUsd * minutes, 4, MidpointRounding.AwayFromZero)
        };
    }
}
