namespace HealthPilot.Api.Models;

public class EstimateAuditLog
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public string Insurer { get; set; } = string.Empty;
    public string CptCode { get; set; } = string.Empty;
    public decimal NegotiatedRateUsed { get; set; }
    public decimal DeductibleRemaining { get; set; }
    public decimal CoinsurancePercent { get; set; }
    public decimal Copay { get; set; }
    public decimal OopMaxRemaining { get; set; }
    public bool CopayAppliesBeforeDeductible { get; set; }
    public decimal EstimatedPatientResponsibility { get; set; }
    public decimal InsurerPayment { get; set; }
    public string BenefitLogicVersion { get; set; } = "1.0";
}