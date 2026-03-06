namespace HealthPilot.Api.Ingestion.Policies;

public interface IPolicySourceFetcher
{
    Task<PolicySourceDocument> FetchAsync(string sourceUrl, CancellationToken cancellationToken);
}
