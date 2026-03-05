namespace HealthPilot.Api.Models;

/// <summary>
/// Represents the negotiated (contracted) rate between an insurer and a facility for a specific procedure.
/// The composite primary key is <c>(ProcedureId, FacilityId, InsurerId)</c>.
/// </summary>
public class NegotiatedRate
{
    /// <summary>Foreign key referencing <see cref="Procedure.Id"/>.</summary>
    public int ProcedureId { get; set; }

    /// <summary>Foreign key referencing <see cref="Facility.Id"/>.</summary>
    public int FacilityId { get; set; }

    /// <summary>Foreign key referencing <see cref="Insurer.Id"/>.</summary>
    public int InsurerId { get; set; }

    /// <summary>The contracted rate amount, stored with two decimal places.</summary>
    public decimal Rate { get; set; }

    /// <summary>Rate type descriptor (e.g. "contracted", "fee_schedule", "per_diem").</summary>
    public required string RateType { get; set; }

    /// <summary>UTC timestamp of the most recent update to this negotiated-rate record.</summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>Navigation property to the associated <see cref="Models.Procedure"/>.</summary>
    public Procedure Procedure { get; set; } = null!;

    /// <summary>Navigation property to the associated <see cref="Models.Facility"/>.</summary>
    public Facility Facility { get; set; } = null!;

    /// <summary>Navigation property to the associated <see cref="Models.Insurer"/>.</summary>
    public Insurer Insurer { get; set; } = null!;
}
