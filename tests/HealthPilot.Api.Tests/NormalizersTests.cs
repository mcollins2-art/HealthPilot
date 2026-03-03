using HealthPilot.Api.Ingestion;
using Xunit;

namespace HealthPilot.Api.Tests;

public class NormalizersTests
{
    [Theory]
    [InlineData("70553", "70553")]
    [InlineData(" 705-53 ", "70553")]
    [InlineData("a1234", "A1234")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("**cpt_99**", "CPT99")]
    public void NormalizeCptCode_StripsNonAlphanumeric_AndUppercases(string? raw, string expected)
    {
        var normalized = Normalizers.NormalizeCptCode(raw!);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("Aetna", "AETNA")]
    [InlineData("  blue   cross  ", "BLUE CROSS")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("United\tHealthcare", "UNITED HEALTHCARE")]
    public void NormalizeInsurerName_TrimsCollapsesWhitespace_AndUppercases(string? raw, string expected)
    {
        var normalized = Normalizers.NormalizeInsurerName(raw!);

        Assert.Equal(expected, normalized);
    }
}
