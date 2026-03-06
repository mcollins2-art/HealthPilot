namespace HealthPilot.Api.Services;

public record PricingSummary(
    decimal? NegotiatedMin,
    decimal? NegotiatedMax,
    decimal? CashMin,
    decimal? CashMax,
    int MatchedFacilityCount = 0,
    string? MatchedInsurer = null,
    DateTimeOffset? DataAsOfDate = null
);
