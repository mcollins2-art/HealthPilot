namespace HealthPilot.Api.Ingestion.Policies;

public interface IPolicyDocumentParser
{
    string ParseToText(PolicySourceDocument sourceDocument);
}
