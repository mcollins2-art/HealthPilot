using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Ingestion.Parsers;

/// <summary>
/// Parses a pricing file into a list of <see cref="StructuredPricingRecord"/> instances.
/// Implementations are selected based on file extension by <see cref="PricingIngestionPipeline"/>.
/// </summary>
public interface IPricingParser
{
    /// <summary>
    /// Parses the file at <paramref name="filePath"/> and returns all extractable pricing records.
    /// </summary>
    /// <param name="filePath">Absolute path to the pricing file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of normalized pricing records.</returns>
    Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken);
}
