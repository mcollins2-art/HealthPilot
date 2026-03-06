namespace HealthPilot.Api.Ingestion;

internal static class IngestionErrorFormatter
{
    private const int MaxErrorMessageLength = 2048;
    private const string TruncatedSuffix = "...[truncated]";

    public static string BuildBoundedErrorMessage(Exception ex, string correlationId)
    {
        var message = $"{ex.GetType().Name}: {ex.Message} | correlationId={correlationId}";
        if (message.Length <= MaxErrorMessageLength)
        {
            return message;
        }

        var maxPrefixLength = MaxErrorMessageLength - TruncatedSuffix.Length;
        return $"{message[..maxPrefixLength]}{TruncatedSuffix}";
    }
}
