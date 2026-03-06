namespace HealthPilot.Api.Models;

/// <summary>
/// Represents a self-pay (cash) price published by a facility for a specific procedure.
/// The composite key is <c>(ProcedureId, FacilityId)</c>.
/// </summary>
public class CashPrice
{
    /// <summary>Foreign key referencing <see cref="Procedure.Id"/>.</summary>
    public int ProcedureId { get; set; }

    /// <summary>Foreign key referencing <see cref="Facility.Id"/>.</summary>
    public int FacilityId { get; set; }

    /// <summary>The self-pay price amount, stored with two decimal places.</summary>
    public decimal CashPriceAmount { get; set; }

    /// <summary>UTC timestamp of the most recent update to this cash-price record.</summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>Navigation property to the associated <see cref="Models.Procedure"/>.</summary>
    public Procedure Procedure { get; set; } = null!;

    /// <summary>Navigation property to the associated <see cref="Models.Facility"/>.</summary>
    public Facility Facility { get; set; } = null!;
}
