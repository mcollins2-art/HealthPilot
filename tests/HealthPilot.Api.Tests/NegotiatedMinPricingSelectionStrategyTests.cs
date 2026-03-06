using HealthPilot.Api.Services;
using Xunit;

namespace HealthPilot.Api.Tests;

public class NegotiatedMinPricingSelectionStrategyTests
{
    private readonly NegotiatedMinPricingSelectionStrategy _strategy = new();

    [Fact]
    public void PolicyName_ReturnsNegotiatedMin()
    {
        Assert.Equal("negotiated_min", _strategy.PolicyName);
    }

    [Fact]
    public void SelectRepresentativeRate_ReturnsNegotiatedMin_WhenPresent()
    {
        var summary = new PricingSummary(NegotiatedMin: 900m, NegotiatedMax: 1400m, CashMin: 500m, CashMax: 800m);

        var rate = _strategy.SelectRepresentativeRate(summary);

        Assert.Equal(900m, rate);
    }

    [Fact]
    public void SelectRepresentativeRate_ReturnsZero_WhenNegotiatedMinIsNull()
    {
        var summary = new PricingSummary(NegotiatedMin: null, NegotiatedMax: null, CashMin: 500m, CashMax: 800m);

        var rate = _strategy.SelectRepresentativeRate(summary);

        Assert.Equal(0m, rate);
    }

    [Fact]
    public void SelectRepresentativeRate_ReturnsZero_WhenAllValuesNull()
    {
        var summary = new PricingSummary(null, null, null, null);

        var rate = _strategy.SelectRepresentativeRate(summary);

        Assert.Equal(0m, rate);
    }

    [Fact]
    public void SelectRepresentativeRate_ReturnsMinNotMax_WhenBothPresent()
    {
        var summary = new PricingSummary(NegotiatedMin: 950m, NegotiatedMax: 1400m, CashMin: null, CashMax: null);

        var rate = _strategy.SelectRepresentativeRate(summary);

        Assert.Equal(950m, rate);
        Assert.NotEqual(1400m, rate);
    }
}
