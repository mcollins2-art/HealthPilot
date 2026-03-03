using HealthPilot.Api.Ingestion.Parsers;
using Xunit;

namespace HealthPilot.Api.Tests;

public class CmsJsonPricingParserTests : IDisposable
{
    private readonly string _tempDirectory;

    public CmsJsonPricingParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-json-parser-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task ParseAsync_ReturnsEmpty_WhenRootIsNotArray()
    {
        var filePath = Path.Combine(_tempDirectory, "object.json");
        await File.WriteAllTextAsync(filePath, """
        {
          "cpt_code": "70551",
          "description": "Brain MRI"
        }
        """);

        var parser = new CmsJsonPricingParser();
        var records = await parser.ParseAsync(filePath, CancellationToken.None);

        Assert.Empty(records);
    }

    [Fact]
    public async Task ParseAsync_MapsValidRows_AndSkipsMissingCpt()
    {
        var filePath = Path.Combine(_tempDirectory, "array.json");
        await File.WriteAllTextAsync(filePath, """
        [
          {
            "cpt_code": "",
            "description": "Invalid Row"
          },
          {
            "cpt_code": "70551",
            "description": "Brain MRI",
            "category": "imaging",
            "facility_name": "Hospital A",
            "facility_type": "hospital",
            "city": "Hoboken",
            "state": "NJ",
            "zip": "07030",
            "insurer": "Plan A",
            "negotiated_rate": 1234.56,
            "rate_type": "contracted",
            "cash_price": "987.65"
          },
          {
            "cpt_code": "70450",
            "description": 123,
            "city": null,
            "negotiated_rate": "not-a-number",
            "cash_price": 700
          },
          {
            "cpt_code": "71020",
            "description": "Chest X-Ray",
            "category": "imaging",
            "facility_name": "Hospital C",
            "facility_type": "hospital",
            "city": "Newark",
            "state": "NJ",
            "zip": "07102",
            "insurer": "Plan C"
          }
        ]
        """);

        var parser = new CmsJsonPricingParser();
        var records = await parser.ParseAsync(filePath, CancellationToken.None);

        Assert.Equal(3, records.Count);

        var first = records[0];
        Assert.Equal("70551", first.CptCode);
        Assert.Equal("Brain MRI", first.ProcedureDescription);
        Assert.Equal(1234.56m, first.NegotiatedRate);
        Assert.Equal(987.65m, first.CashPrice);
        Assert.Equal("PLAN A", first.InsurerName);

        var second = records[1];
        Assert.Equal("70450", second.CptCode);
        Assert.Equal("123", second.ProcedureDescription);
        Assert.Equal(string.Empty, second.City);
        Assert.Null(second.NegotiatedRate);
        Assert.Equal(700m, second.CashPrice);
        Assert.Equal("Unknown Facility", second.FacilityName);
        Assert.Equal("imaging", second.ProcedureCategory);

        var third = records[2];
        Assert.Equal("71020", third.CptCode);
        Assert.Null(third.NegotiatedRate);
        Assert.Null(third.CashPrice);
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
