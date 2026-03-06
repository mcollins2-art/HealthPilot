namespace HealthPilot.Api.Dtos;

public class CheapestProviderResponse
{
    public required string ProviderName { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string ZipCode { get; set; }
    public decimal? NegotiatedRate { get; set; }
    public decimal? CashPrice { get; set; }
    public decimal SelectedPrice { get; set; }
}
