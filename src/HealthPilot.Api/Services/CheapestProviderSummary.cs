namespace HealthPilot.Api.Services;

public record CheapestProviderSummary(
    string ProviderName,
    string City,
    string State,
    string ZipCode,
    decimal? NegotiatedRate,
    decimal? CashPrice,
    decimal SelectedPrice
);
