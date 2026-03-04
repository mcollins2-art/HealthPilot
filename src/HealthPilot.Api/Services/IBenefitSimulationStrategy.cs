namespace HealthPilot.Api.Services;

public interface IBenefitSimulationStrategy
{
    string Version { get; }
    BenefitSimulationResult Simulate(BenefitSimulationInput input);
}
