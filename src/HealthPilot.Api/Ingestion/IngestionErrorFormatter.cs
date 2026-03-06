namespace HealthPilot.Api.Ingestion;

internal static class IngestionErrorFormatter
{
    private const int MaxErrorMessageLength = 2048;

    public static string BuildBoundedErrorMessage(Exception ex, string correlationId)
    {
        var message = $"{ex.GetType().Name}: {ex.Message} | correlationId={correlationId}";
        return message.Length <= MaxErrorMessageLength ? message : message[..MaxErrorMessageLength];
    }
}
