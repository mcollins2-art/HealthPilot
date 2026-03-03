namespace HealthPilot.Api.Services;

public static class MonetaryPolicy
{
    public const int Scale = 2;
    public const MidpointRounding RoundingMode = MidpointRounding.AwayFromZero;
    public const string PolicyVersion = "1.1";

    public static decimal Round(decimal amount) => decimal.Round(amount, Scale, RoundingMode);
}
