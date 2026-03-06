using HealthPilot.Api.Models;

namespace HealthPilot.Api.Ingestion.Policies;

public interface IPolicyPersistenceService
{
    Task<Insurer> GetOrCreateInsurerAsync(string insurerName, CancellationToken cancellationToken);
    Task<PolicyIngestionResult> PersistAsync(PersistPolicyInput input, CancellationToken cancellationToken);
}
