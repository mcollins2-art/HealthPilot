namespace HealthPilot.Api.Services;

public sealed class NegotiatedMinPricingSelectionStrategy : IPricingSelectionStrategy
{
    public const string Version = "negotiated_min";

    public string PolicyName => Version;

    public decimal SelectRepresentativeRate(PricingSummary pricingSummary)
    {
        return pricingSummary.NegotiatedMin ?? 0m;
    }
}
