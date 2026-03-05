using System.ComponentModel.DataAnnotations;

namespace HealthPilot.Api.Dtos;

/// <summary>
/// Request body for the <c>POST /ingestion/import</c> endpoint.
/// Specifies the file to import along with optional batching and scheduling parameters.
/// </summary>
public class IngestionImportRequest : IValidatableObject
{
    /// <summary>
    /// Absolute or relative path to the pricing file (.csv or .json).
    /// The resolved path must reside within the configured <c>Ingestion:AllowedRootPath</c> if set.
    /// </summary>
    [Required]
    [MinLength(3)]
    [MaxLength(1024)]
    public required string FilePath { get; set; }

    /// <summary>
    /// Number of records to accumulate before flushing each persistence batch.
    /// Must be between 1 and 50,000. Defaults to the configured <c>Ingestion:BatchSize</c> (5,000).
    /// </summary>
    [Range(1, 50000)]
    public int? BatchSize { get; set; }

    /// <summary>
    /// When <c>true</c> (default), import resumes from the last saved checkpoint for this file
    /// rather than re-processing rows that were already persisted.
    /// </summary>
    public bool ResumeFromCheckpoint { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the import is queued and processed asynchronously; the endpoint returns
    /// <c>202 Accepted</c> with a job ID. When <c>false</c> (default), the import runs synchronously.
    /// </summary>
    public bool Async { get; set; }

    /// <summary>Optional free-text identifier for the upstream system providing the file (e.g. "cms_mrf_2026").</summary>
    [MaxLength(100)]
    public string? SourceSystem { get; set; }

    /// <summary>UTC timestamp from which the pricing data in this file is considered effective.</summary>
    public DateTimeOffset? EffectiveStartUtc { get; set; }

    /// <summary>UTC timestamp after which the pricing data in this file is no longer effective.</summary>
    public DateTimeOffset? EffectiveEndUtc { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EffectiveStartUtc.HasValue && EffectiveEndUtc.HasValue
            && EffectiveEndUtc.Value <= EffectiveStartUtc.Value)
        {
            yield return new ValidationResult(
                "EffectiveEndUtc must be later than EffectiveStartUtc.",
                [nameof(EffectiveEndUtc)]);
        }
    }
}
