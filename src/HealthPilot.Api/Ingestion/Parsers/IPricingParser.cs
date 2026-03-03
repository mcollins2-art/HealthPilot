using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Ingestion.Parsers;

public interface IPricingParser
{
    Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken);
}
