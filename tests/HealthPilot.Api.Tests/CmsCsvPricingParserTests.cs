using HealthPilot.Api.Ingestion.Parsers;
using Xunit;

namespace HealthPilot.Api.Tests;

public class CmsCsvPricingParserTests : IDisposable
{
    private readonly string _tempDirectory;

    public CmsCsvPricingParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-csv-parser-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ParseAsync_MapsValidRows_AndSkipsMissingCpt()
    {
        var filePath = Path.Combine(_tempDirectory, "parser.csv");
        await File.WriteAllTextAsync(filePath,
            "cpt_code,description,category,facility_name,facility_type,city,state,zip,insurer,negotiated_rate,rate_type,cash_price,last_updated\n" +
            ",Missing CPT,imaging,Hospital A,hospital,Hoboken,NJ,07030,Plan A,1000,contracted,900,\n" +
            "70551,Brain MRI,imaging,Hospital B,hospital,Hoboken,NJ,07030, Plan A ,1200.50,contracted,980.25,2026-01-01T10:00:00-05:00\n" +
            "70450,Head CT,,Hospital C,hospital,,NJ,07031,Plan B,not-a-number,case_rate,,");

        var parser = new CmsCsvPricingParser();
        var rows = await parser.ParseAsync(filePath, CancellationToken.None);

        Assert.Equal(2, rows.Count);

        var first = rows[0];
        Assert.Equal("70551", first.CptCode);
        Assert.Equal("Brain MRI", first.ProcedureDescription);
        Assert.Equal(1200.50m, first.NegotiatedRate);
        Assert.Equal(980.25m, first.CashPrice);
        Assert.Equal("PLAN A", first.InsurerName);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.Zero), first.LastUpdated);

        var second = rows[1];
        Assert.Equal("70450", second.CptCode);
        Assert.Equal("Head CT", second.ProcedureDescription);
        Assert.Equal(string.Empty, second.ProcedureCategory);
        Assert.Equal(string.Empty, second.City);
        Assert.Null(second.NegotiatedRate);
        Assert.Null(second.CashPrice);
        Assert.Equal("case_rate", second.NegotiatedRateType);
    }

    [Fact]
    public async Task ParseAsync_UsesFallbacks_WhenOptionalColumnsMissing()
    {
        var filePath = Path.Combine(_tempDirectory, "minimal.csv");
        await File.WriteAllTextAsync(filePath,
            "cpt_code\n" +
            "71020");

        var parser = new CmsCsvPricingParser();
        var rows = await parser.ParseAsync(filePath, CancellationToken.None);

        var item = Assert.Single(rows);
        Assert.Equal("71020", item.CptCode);
        Assert.Equal("Unknown Procedure", item.ProcedureDescription);
        Assert.Equal("imaging", item.ProcedureCategory);
        Assert.Equal("Unknown Facility", item.FacilityName);
        Assert.Equal("hospital", item.FacilityType);
        Assert.Equal(string.Empty, item.InsurerName);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
