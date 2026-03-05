using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Services;

/// <summary>
/// Persists parsed pricing records into the normalized pricing tables using an upsert strategy
/// that creates reference data (procedures, facilities, insurers) on first encounter.
/// </summary>
public interface IPricingPersistenceService
{
    /// <summary>
    /// Upserts a batch of structured pricing records into the database.
    /// Reference entities (procedures, facilities, insurers) are created if they do not already exist.
    /// </summary>
    /// <param name="records">The parsed and normalized pricing records to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="PricingPersistenceResult"/> with counts of created and updated entities.</returns>
    Task<PricingPersistenceResult> UpsertPricingDataAsync(
        IReadOnlyList<StructuredPricingRecord> records,
        CancellationToken cancellationToken);
}
