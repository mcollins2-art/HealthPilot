using System.ComponentModel.DataAnnotations;
using HealthPilot.Api.Dtos;
using Xunit;

namespace HealthPilot.Api.Tests;

public class IngestionImportRequestTests
{
    [Fact]
    public void Validate_ReturnsNoErrors_WhenOnlyStartUtcProvided()
    {
        var request = new IngestionImportRequest
        {
            FilePath = "/data/file.csv",
            EffectiveStartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var errors = ValidateModel(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsNoErrors_WhenOnlyEndUtcProvided()
    {
        var request = new IngestionImportRequest
        {
            FilePath = "/data/file.csv",
            EffectiveEndUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)
        };

        var errors = ValidateModel(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsNoErrors_WhenEndUtcAfterStartUtc()
    {
        var request = new IngestionImportRequest
        {
            FilePath = "/data/file.csv",
            EffectiveStartUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveEndUtc = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero)
        };

        var errors = ValidateModel(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsError_WhenEndUtcEqualsStartUtc()
    {
        var timestamp = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var request = new IngestionImportRequest
        {
            FilePath = "/data/file.csv",
            EffectiveStartUtc = timestamp,
            EffectiveEndUtc = timestamp
        };

        var errors = ValidateModel(request);

        Assert.Single(errors);
        Assert.Contains("EffectiveEndUtc", errors[0].MemberNames);
        Assert.Contains("later than", errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ReturnsError_WhenEndUtcBeforeStartUtc()
    {
        var request = new IngestionImportRequest
        {
            FilePath = "/data/file.csv",
            EffectiveStartUtc = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            EffectiveEndUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var errors = ValidateModel(request);

        Assert.Single(errors);
        Assert.Contains("EffectiveEndUtc", errors[0].MemberNames);
    }

    [Fact]
    public void Validate_ReturnsFilePath_WhenTooShort()
    {
        var request = new IngestionImportRequest { FilePath = "x" };

        var errors = ValidateModel(request);

        Assert.True(errors.Count > 0);
    }

    private static List<ValidationResult> ValidateModel(IngestionImportRequest request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }
}
