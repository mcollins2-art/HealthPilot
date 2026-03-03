namespace HealthPilot.Api.Models;

public class Procedure
{
    public int Id { get; set; }
    public required string CptCode { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }

    public ICollection<NegotiatedRate> NegotiatedRates { get; set; } = new List<NegotiatedRate>();
    public ICollection<CashPrice> CashPrices { get; set; } = new List<CashPrice>();
}
