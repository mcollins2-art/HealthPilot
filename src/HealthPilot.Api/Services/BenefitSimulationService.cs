namespace HealthPilot.Api.Services;

public class BenefitSimulationService : IBenefitSimulationService
{
    private readonly Dictionary<string, IBenefitSimulationStrategy> _strategies;
    private readonly string _configuredVersion;

    public BenefitSimulationService()
        : this([new BenefitSimulationStrategyV1()], "v1")
    {
    }

    public BenefitSimulationService(
        IEnumerable<IBenefitSimulationStrategy> strategies,
        IConfiguration configuration)
        : this(strategies, configuration["BenefitSimulation:LogicVersion"])
    {
    }

    private BenefitSimulationService(
        IEnumerable<IBenefitSimulationStrategy> strategies,
        string? configuredVersion)
    {
        _strategies = strategies
            .GroupBy(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        if (_strategies.Count == 0)
        {
            throw new InvalidOperationException("At least one benefit simulation strategy must be registered.");
        }

        _configuredVersion = string.IsNullOrWhiteSpace(configuredVersion) ? "v1" : configuredVersion.Trim();
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

        if (_strategies.TryGetValue("v1", out var defaultStrategy))
        {
            return defaultStrategy;
        }

        return _strategies.Values.First();
    }
}
