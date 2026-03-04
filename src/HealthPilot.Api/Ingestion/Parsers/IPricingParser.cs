using HealthPilot.Api.Dtos;

namespace HealthPilot.Api.Ingestion.Parsers;

public interface IPricingParser
{
    IReadOnlyCollection<string> SupportedExtensions { get; }
    Task<IReadOnlyList<StructuredPricingRecord>> ParseAsync(string filePath, CancellationToken cancellationToken);
}
