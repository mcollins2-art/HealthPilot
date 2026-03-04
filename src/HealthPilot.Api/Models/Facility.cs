namespace HealthPilot.Api.Models;

public class Facility
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string Zip { get; set; }
    public string TenantId { get; set; } = string.Empty;

    public ICollection<NegotiatedRate> NegotiatedRates { get; set; } = new List<NegotiatedRate>();
    public ICollection<CashPrice> CashPrices { get; set; } = new List<CashPrice>();
}
