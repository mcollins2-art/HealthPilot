namespace HealthPilot.Api.Services;

public class PricingPersistenceResult
{
    public int RecordsReceived { get; set; }
    public int RecordsSkipped { get; set; }

    public int ProceduresCreated { get; set; }
    public int FacilitiesCreated { get; set; }
    public int InsurersCreated { get; set; }

    public int NegotiatedRatesUpserted { get; set; }
    public int CashPricesUpserted { get; set; }
}
