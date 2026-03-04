namespace HealthPilot.Api.Ingestion.Parsers;

public interface IPricingParserRegistry
{
    IPricingParser ResolveByExtension(string extension);
}
