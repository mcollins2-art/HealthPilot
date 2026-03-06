using HealthPilot.Api.Dtos;
using HealthPilot.Api.Services;
using Xunit;

namespace HealthPilot.Api.Tests;

public class OutpatientSurgeryEstimateServiceTests
{
    [Fact]
    public async Task EstimateAsync_ReturnsProjectedResponse_FromPricingAndBenefits()
    {
        var service = new OutpatientSurgeryEstimateService(
            new StubPricingQueryService(),
            new StubPricingSelectionStrategy(),
            new StubBenefitSimulationService());

        var response = await service.EstimateAsync(new OutpatientSurgeryEstimateRequest
        {
            ZipCode = "10001",
            Insurer = "Aetna",
            CptCode = "47562",
            DeductibleRemaining = 1200m,
            CoinsurancePercent = 20m,
            Copay = 50m,
            OopMaxRemaining = 3000m,
            CopayAppliesBeforeDeductible = true
        }, CancellationToken.None);

        Assert.Equal(1500m, response.NegotiatedRateMin);
        Assert.Equal(2400m, response.NegotiatedRateMax);
        Assert.Equal("$1500.00 - $2400.00", response.NegotiatedRateRange);
        Assert.Equal(515m, response.EstimatedOutOfPocket);
        Assert.Equal(1300m, response.CashPriceMin);
        Assert.Equal(2100m, response.CashPriceMax);
        Assert.Equal("$1300.00 - $2100.00", response.CashPriceRange);
        Assert.Equal(1685m, response.InsurerPaymentEstimate);
        Assert.Equal("AwayFromZero", response.RoundingMode);
    }

    private sealed class StubPricingQueryService : IPricingQueryService
    {
        public Task<PricingSummary> GetPricingSummaryAsync(string zipCode, string insurer, string cptCode, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PricingSummary(1500m, 2400m, 1300m, 2100m));
        }

        public string FormatRange(decimal? minValue, decimal? maxValue)
        {
            if (minValue is null || maxValue is null)
            {
                return "N/A";
            }

            return $"${minValue.Value:F2} - ${maxValue.Value:F2}";
        }
    }

    private sealed class StubPricingSelectionStrategy : IPricingSelectionStrategy
    {
        public string PolicyName => "test";

        public decimal SelectRepresentativeRate(PricingSummary pricingSummary)
        {
            return 2200m;
        }
    }

    private sealed class StubBenefitSimulationService : IBenefitSimulationService
    {
        public BenefitSimulationResult Simulate(BenefitSimulationInput input)
        {
            return new BenefitSimulationResult(515m, 1685m);
        }
    }
}
