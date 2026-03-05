namespace HealthPilot.Api.Services;

/// <summary>
/// Computes patient and insurer payment responsibility given benefit plan parameters.
/// </summary>
public interface IBenefitSimulationService
{
    /// <summary>
    /// Simulates benefit adjudication for a single service, returning the patient's estimated
    /// out-of-pocket cost and the insurer's estimated payment.
    /// </summary>
    /// <param name="input">Negotiated rate and benefit parameters for the simulation.</param>
    /// <returns>A <see cref="BenefitSimulationResult"/> containing estimated patient and insurer costs.</returns>
    BenefitSimulationResult Simulate(BenefitSimulationInput input);
}
