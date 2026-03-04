using HealthPilot.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HealthPilot.Api.Tests;

public class BenefitSimulationServiceTests
{
	private readonly BenefitSimulationService _service = new();

	[Fact]
	public void Simulate_WhenPartialDeductible_RemainingUsesCoinsurance()
	{
		var input = new BenefitSimulationInput(1000m, 300m, 20m, 0m, 5000m, true);

		var result = _service.Simulate(input);

		Assert.Equal(440m, result.EstimatedPatientResponsibility);
		Assert.Equal(560m, result.InsurerPayment);
	}

	[Fact]
	public void Simulate_WhenOopRemainingZero_PatientPaysNothing()
	{
		var input = new BenefitSimulationInput(1250m, 500m, 20m, 30m, 0m, true);

		var result = _service.Simulate(input);

		Assert.Equal(0m, result.EstimatedPatientResponsibility);
		Assert.Equal(1250m, result.InsurerPayment);
	}

	[Fact]
	public void Simulate_WhenCopayBeforeDeductible_DiffersFromDeductibleFirst()
	{
		var beforeDeductible = _service.Simulate(new BenefitSimulationInput(1000m, 200m, 20m, 100m, 5000m, true));
		var deductibleFirst = _service.Simulate(new BenefitSimulationInput(1000m, 200m, 20m, 100m, 5000m, false));

		Assert.NotEqual(beforeDeductible.EstimatedPatientResponsibility, deductibleFirst.EstimatedPatientResponsibility);
	}

	[Theory]
	[InlineData(0, 0, 0, 0, 1000)]
	[InlineData(200, 0, 0, 0, 1000)]
	[InlineData(200, 100, 0, 0, 1000)]
	[InlineData(200, 100, 20, 0, 1000)]
	[InlineData(200, 100, 20, 30, 1000)]
	[InlineData(500, 50, 10, 0, 1000)]
	[InlineData(500, 50, 10, 20, 1000)]
	[InlineData(500, 250, 10, 20, 1000)]
	[InlineData(500, 250, 40, 20, 1000)]
	[InlineData(500, 600, 40, 20, 1000)]
	[InlineData(750, 0, 15, 0, 1000)]
	[InlineData(750, 100, 15, 0, 1000)]
	[InlineData(750, 100, 15, 25, 1000)]
	[InlineData(750, 300, 15, 25, 1000)]
	[InlineData(750, 300, 40, 25, 1000)]
	[InlineData(1000, 0, 20, 0, 1000)]
	[InlineData(1000, 100, 20, 0, 1000)]
	[InlineData(1000, 100, 20, 50, 1000)]
	[InlineData(1000, 500, 20, 50, 1000)]
	[InlineData(1000, 500, 35, 50, 1000)]
	[InlineData(1200, 0, 20, 40, 2000)]
	[InlineData(1200, 200, 20, 40, 2000)]
	[InlineData(1200, 600, 20, 40, 2000)]
	[InlineData(1200, 600, 50, 40, 2000)]
	[InlineData(1500, 0, 30, 0, 2000)]
	[InlineData(1500, 200, 30, 0, 2000)]
	[InlineData(1500, 200, 30, 60, 2000)]
	[InlineData(1500, 800, 30, 60, 2000)]
	[InlineData(1500, 800, 50, 60, 2000)]
	[InlineData(2000, 0, 25, 0, 3000)]
	[InlineData(2000, 300, 25, 0, 3000)]
	[InlineData(2000, 300, 25, 80, 3000)]
	[InlineData(2000, 900, 25, 80, 3000)]
	[InlineData(2000, 900, 40, 80, 3000)]
	public void Simulate_MoneyBalanceIsMaintained_CopayBeforeDeductible(
		decimal negotiatedRate,
		decimal deductibleRemaining,
		decimal coinsurancePercent,
		decimal copay,
		decimal oopMaxRemaining)
	{
		var result = _service.Simulate(new BenefitSimulationInput(
			negotiatedRate,
			deductibleRemaining,
			coinsurancePercent,
			copay,
			oopMaxRemaining,
			true));

		Assert.Equal(decimal.Round(Math.Max(negotiatedRate, 0m), 2), decimal.Round(result.EstimatedPatientResponsibility + result.InsurerPayment, 2));
		Assert.True(result.EstimatedPatientResponsibility >= 0m);
		Assert.True(result.InsurerPayment >= 0m);
	}

	[Theory]
	[InlineData(1000, 1000, 20, 0, 400)]
	[InlineData(1000, 0, 100, 0, 300)]
	[InlineData(1000, 500, 50, 100, 450)]
	[InlineData(1200, 1000, 50, 100, 500)]
	[InlineData(1500, 1000, 30, 120, 350)]
	[InlineData(2000, 1000, 50, 200, 250)]
	[InlineData(900, 900, 0, 50, 100)]
	[InlineData(900, 100, 100, 50, 200)]
	[InlineData(1300, 400, 40, 75, 325)]
	[InlineData(700, 300, 20, 40, 150)]
	public void Simulate_RespectsOopCap(
		decimal negotiatedRate,
		decimal deductibleRemaining,
		decimal coinsurancePercent,
		decimal copay,
		decimal oopMaxRemaining)
	{
		var result = _service.Simulate(new BenefitSimulationInput(
			negotiatedRate,
			deductibleRemaining,
			coinsurancePercent,
			copay,
			oopMaxRemaining,
			false));

		Assert.True(result.EstimatedPatientResponsibility <= oopMaxRemaining);
	}

	[Theory]
	[InlineData(-1, 100, 20, 10, 1000)]
	[InlineData(1000, -1, 20, 10, 1000)]
	[InlineData(1000, 100, -1, 10, 1000)]
	[InlineData(1000, 100, 101, 10, 1000)]
	[InlineData(1000, 100, 20, -1, 1000)]
	[InlineData(1000, 100, 20, 10, -1)]
	[InlineData(-1000, -100, -20, -10, -500)]
	[InlineData(0, -100, 200, -10, 100)]
	[InlineData(-100, 100, 20, 10, 200)]
	[InlineData(500, 500, 500, 500, 500)]
	public void Simulate_ClampsMalformedInputs(
		decimal negotiatedRate,
		decimal deductibleRemaining,
		decimal coinsurancePercent,
		decimal copay,
		decimal oopMaxRemaining)
	{
		var result = _service.Simulate(new BenefitSimulationInput(
			negotiatedRate,
			deductibleRemaining,
			coinsurancePercent,
			copay,
			oopMaxRemaining,
			true));

		Assert.True(result.EstimatedPatientResponsibility >= 0m);
		Assert.True(result.InsurerPayment >= 0m);
	}

	[Theory]
	[InlineData(333.335, 100, 20, 0, 1000)]
	[InlineData(333.336, 100, 20, 0, 1000)]
	[InlineData(99.999, 50, 10, 5, 1000)]
	[InlineData(999.999, 200, 15, 25, 1000)]
	[InlineData(1234.567, 300, 30, 40, 2000)]
	[InlineData(1.005, 0.505, 12.5, 0.25, 5.25)]
	public void Simulate_RoundsToTwoDecimalPlaces(
		decimal negotiatedRate,
		decimal deductibleRemaining,
		decimal coinsurancePercent,
		decimal copay,
		decimal oopMaxRemaining)
	{
		var result = _service.Simulate(new BenefitSimulationInput(
			negotiatedRate,
			deductibleRemaining,
			coinsurancePercent,
			copay,
			oopMaxRemaining,
			true));

		Assert.Equal(result.EstimatedPatientResponsibility, decimal.Round(result.EstimatedPatientResponsibility, 2));
		Assert.Equal(result.InsurerPayment, decimal.Round(result.InsurerPayment, 2));
	}

	[Fact]
	public void Simulate_UsesAwayFromZeroRoundingPolicy()
	{
		var result = _service.Simulate(new BenefitSimulationInput(1.005m, 0m, 100m, 0m, 10m, true));

		Assert.Equal(1.01m, result.EstimatedPatientResponsibility);
	}

	[Fact]
	public void LogicVersion_UsesConfiguredStrategyVersion()
	{
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["BenefitSimulation:LogicVersion"] = "v-test" })
			.Build();
		var service = new BenefitSimulationService(new[] { new TestBenefitSimulationStrategy("v-test") }, config);

		Assert.Equal("v-test", service.LogicVersion);
		Assert.Equal(12m, service.Simulate(new BenefitSimulationInput(0m, 0m, 0m, 0m, 0m)).EstimatedPatientResponsibility);
	}

	private sealed class TestBenefitSimulationStrategy(string version) : IBenefitSimulationStrategy
	{
		public string Version { get; } = version;

		public BenefitSimulationResult Simulate(BenefitSimulationInput input)
		{
			return new BenefitSimulationResult(12m, 34m);
		}
	}
}
