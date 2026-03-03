using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

public interface IPricingPersistenceService
{
    Task<PricingPersistenceResult> UpsertPricingDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        long? ingestionJobId,
        CancellationToken cancellationToken);
}
