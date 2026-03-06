namespace HealthPilot.Api.Ingestion.Policies;

public class PolicySourceFetcher(HttpClient httpClient) : IPolicySourceFetcher
{
    public async Task<PolicySourceDocument> FetchAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(sourceUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/plain";

        return new PolicySourceDocument(sourceUrl, contentType, content);
    }
}
