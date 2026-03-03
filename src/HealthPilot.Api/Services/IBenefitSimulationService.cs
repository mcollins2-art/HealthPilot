namespace HealthPilot.Api.Services;

public interface IBenefitSimulationService
{
    BenefitSimulationResult Simulate(BenefitSimulationInput input);
}
