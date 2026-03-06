namespace HealthPilot.Api.Dtos;

public class EstimateResponse
{
    public decimal? NegotiatedRateMin { get; set; }
    public decimal? NegotiatedRateMax { get; set; }
    public required string NegotiatedRateRange { get; set; }
    public decimal EstimatedOutOfPocket { get; set; }
    public decimal? CashPriceMin { get; set; }
    public decimal? CashPriceMax { get; set; }
    public required string CashPriceRange { get; set; }
    public decimal InsurerPaymentEstimate { get; set; }
    public required string RoundingMode { get; set; }
    public required string MatchedFacilityCount { get; set; }
    public required string MatchedInsurer { get; set; }
    public DateTimeOffset? DataAsOfDate { get; set; }
}
