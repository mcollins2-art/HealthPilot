namespace HealthPilot.Api.Services;

public class BenefitSimulationService : IBenefitSimulationService
{
    private const string DefaultVersion = "v1";
    private readonly Dictionary<string, IBenefitSimulationStrategy> _strategies;
    private readonly string _configuredVersion;

    public BenefitSimulationService()
        : this(new IBenefitSimulationStrategy[] { new BenefitSimulationStrategyV1() }, DefaultVersion)
    {
    }

    public BenefitSimulationService(
        IEnumerable<IBenefitSimulationStrategy> strategies,
        IConfiguration configuration)
        : this(strategies, GetConfiguredVersion(configuration))
    {
    }

    private BenefitSimulationService(
        IEnumerable<IBenefitSimulationStrategy> strategies,
        string? configuredVersion)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var strategyList = strategies.ToList();
        var duplicateVersions = strategyList
            .GroupBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();
        if (duplicateVersions.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate benefit simulation strategy versions were registered: {string.Join(", ", duplicateVersions)}");
        }

        _strategies = strategyList.ToDictionary(x => x.Version, x => x, StringComparer.OrdinalIgnoreCase);

        if (_strategies.Count == 0)
        {
            throw new InvalidOperationException("At least one benefit simulation strategy must be registered.");
        }

        _configuredVersion = string.IsNullOrWhiteSpace(configuredVersion) ? DefaultVersion : configuredVersion.Trim();
    }

    public string LogicVersion => ResolveStrategy().Version;

    public BenefitSimulationResult Simulate(BenefitSimulationInput input)
    {
        return ResolveStrategy().Simulate(input);
    }

    private IBenefitSimulationStrategy ResolveStrategy()
    {
        if (_strategies.TryGetValue(_configuredVersion, out var configured))
        {
            return configured;
        }

        if (_strategies.TryGetValue(DefaultVersion, out var defaultStrategy))
        {
            return defaultStrategy;
        }

        throw new InvalidOperationException(
            $"Configured benefit simulation strategy '{_configuredVersion}' was not found and no '{DefaultVersion}' fallback strategy is registered.");
    }

    private static string? GetConfiguredVersion(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["BenefitSimulation:LogicVersion"];
    }
}
