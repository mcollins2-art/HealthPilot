namespace HealthPilot.Api.Ingestion.Parsers;

public class PricingParserRegistry(IEnumerable<IPricingParser> parsers) : IPricingParserRegistry
{
    private readonly Dictionary<string, IPricingParser> _parsersByExtension = parsers
        .SelectMany(parser => parser.SupportedExtensions.Select(extension => new
        {
            Extension = extension.ToLowerInvariant(),
            Parser = parser
        }))
        .ToDictionary(x => x.Extension, x => x.Parser, StringComparer.OrdinalIgnoreCase);

    public IPricingParser ResolveByExtension(string extension)
    {
        if (_parsersByExtension.TryGetValue(extension, out var parser))
        {
            return parser;
        }

        throw new NotSupportedException($"Unsupported file extension: {extension}");
    }
}
