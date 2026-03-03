namespace HealthPilot.Api.Dtos;

public class EstimateResponse
{
    public required string NegotiatedRateRange { get; set; }
    public required string EstimatedOutOfPocket { get; set; }
    public required string CashPriceRange { get; set; }
    public required string InsurerPaymentEstimate { get; set; }
}
