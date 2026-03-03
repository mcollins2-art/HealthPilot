namespace HealthPilot.Api.Services;

public interface IPricingSelectionStrategy
{
    string PolicyName { get; }
    decimal SelectRepresentativeRate(PricingSummary pricingSummary);
}
