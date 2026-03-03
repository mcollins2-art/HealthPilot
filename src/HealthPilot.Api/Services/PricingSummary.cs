namespace HealthPilot.Api.Services;

public record PricingSummary(
    decimal? NegotiatedMin,
    decimal? NegotiatedMax,
    decimal? CashMin,
    decimal? CashMax
);
