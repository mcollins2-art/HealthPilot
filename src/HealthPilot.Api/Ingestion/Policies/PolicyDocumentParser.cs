using System.Text.RegularExpressions;

namespace HealthPilot.Api.Ingestion.Policies;

public class PolicyDocumentParser : IPolicyDocumentParser
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public string ParseToText(PolicySourceDocument sourceDocument)
    {
        if (sourceDocument.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return HtmlTagRegex.Replace(sourceDocument.Content, " ");
        }

        if (sourceDocument.ContentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return sourceDocument.Content.Replace('\0', ' ');
        }

        return sourceDocument.Content;
    }
}
