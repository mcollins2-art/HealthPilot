namespace HealthPilot.Api.Models;

/// <summary>
/// Immutable audit record written for each <c>POST /estimate</c> request.
/// Captures the full input context and simulation output for compliance and debugging.
/// </summary>
public class EstimateAuditLog
{
    /// <summary>Auto-generated surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp when this audit record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>ASP.NET Core trace identifier for the originating HTTP request.</summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>ZIP code submitted with the estimate request.</summary>
    public string ZipCode { get; set; } = string.Empty;

    /// <summary>Insurer name submitted with the estimate request.</summary>
    public string Insurer { get; set; } = string.Empty;

    /// <summary>CPT code submitted with the estimate request.</summary>
    public string CptCode { get; set; } = string.Empty;

    /// <summary>Representative negotiated rate selected by the active pricing selection strategy.</summary>
    public decimal NegotiatedRateUsed { get; set; }

    /// <summary>Member's remaining deductible as submitted.</summary>
    public decimal DeductibleRemaining { get; set; }

    /// <summary>Member's coinsurance percentage as submitted (0–100).</summary>
    public decimal CoinsurancePercent { get; set; }

    /// <summary>Fixed copay amount as submitted.</summary>
    public decimal Copay { get; set; }

    /// <summary>Member's remaining out-of-pocket maximum as submitted.</summary>
    public decimal OopMaxRemaining { get; set; }

    /// <summary>Whether copay was applied before the deductible in this simulation.</summary>
    public bool CopayAppliesBeforeDeductible { get; set; }

    /// <summary>Simulated patient out-of-pocket responsibility.</summary>
    public decimal EstimatedPatientResponsibility { get; set; }

    /// <summary>Simulated insurer payment amount.</summary>
    public decimal InsurerPayment { get; set; }

    /// <summary>
    /// Composite version string identifying the monetary policy and pricing selection strategy
    /// (e.g. <c>"1.1:negotiated_min"</c>). Enables future policy changes to be attributed.
    /// </summary>
    public string BenefitLogicVersion { get; set; } = "1.0";
}