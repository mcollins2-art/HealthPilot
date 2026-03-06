namespace HealthPilot.Api.Services;

/// <summary>
/// Accumulates counts of entities created and rows upserted during a pricing persistence operation.
/// </summary>
public class PricingPersistenceResult
{
    /// <summary>Total number of <see cref="Dtos.StructuredPricingRecord"/> instances received by the persistence layer.</summary>
    public int RecordsReceived { get; set; }

    /// <summary>Number of records that were skipped due to missing required fields.</summary>
    public int RecordsSkipped { get; set; }

    /// <summary>Number of new <see cref="Models.Procedure"/> rows inserted during this operation.</summary>
    public int ProceduresCreated { get; set; }

    /// <summary>Number of new <see cref="Models.Facility"/> rows inserted during this operation.</summary>
    public int FacilitiesCreated { get; set; }

    /// <summary>Number of new <see cref="Models.Insurer"/> rows inserted during this operation.</summary>
    public int InsurersCreated { get; set; }

    /// <summary>Number of negotiated-rate rows inserted or updated during this operation.</summary>
    public int NegotiatedRatesUpserted { get; set; }

    /// <summary>Number of cash-price rows inserted or updated during this operation.</summary>
    public int CashPricesUpserted { get; set; }
}
