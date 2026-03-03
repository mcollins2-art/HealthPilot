using HealthPilot.Api.Ingestion;
using Xunit;

namespace HealthPilot.Api.Tests;

public class PricingLoaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public PricingLoaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-pricing-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task LoadJsonAsync_ReturnsRootJsonElement()
    {
        var filePath = Path.Combine(_tempDirectory, "single.json");
        await File.WriteAllTextAsync(filePath, """
        {
          "cpt_code": "70551",
          "negotiated_rate": 1234.56,
          "facility": "Test Imaging"
        }
        """);

        var element = await PricingLoader.LoadJsonAsync(filePath, CancellationToken.None);

        Assert.Equal("70551", element.GetProperty("cpt_code").GetString());
        Assert.Equal(1234.56m, element.GetProperty("negotiated_rate").GetDecimal());
        Assert.Equal("Test Imaging", element.GetProperty("facility").GetString());
    }

    [Fact]
    public void LoadCsv_MapsRowsWithCaseInsensitiveKeys()
    {
        var filePath = Path.Combine(_tempDirectory, "rates.csv");
        File.WriteAllText(filePath, "CPT_Code,Facility,Negotiated_Rate\n70551,Test Imaging,1234.56\n70450,Metro Scan,987.10");

        var rows = PricingLoader.LoadCsv(filePath);

        Assert.Equal(2, rows.Count);
        Assert.Equal("70551", rows[0]["cpt_code"]);
        Assert.Equal("Test Imaging", rows[0]["facility"]);
        Assert.Equal("987.10", rows[1]["NEGOTIATED_RATE"]);
    }

    [Fact]
    public void StreamCsvRows_YieldsEachRecordInOrder()
    {
        var filePath = Path.Combine(_tempDirectory, "stream.csv");
        File.WriteAllText(filePath, "cpt_code,insurer,negotiated_rate\n70551,Plan A,1200\n70450,Plan B,900");

        var rows = PricingLoader.StreamCsvRows(filePath).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal("70551", rows[0]["cpt_code"]);
        Assert.Equal("Plan A", rows[0]["insurer"]);
        Assert.Equal("900", rows[1]["negotiated_rate"]);
    }

    [Fact]
    public void StreamCsvRows_MapsMissingFieldToEmptyString()
    {
        var filePath = Path.Combine(_tempDirectory, "stream-missing-field.csv");
        File.WriteAllText(filePath, "cpt_code,insurer,negotiated_rate\n70551,Plan A,");

        var row = Assert.Single(PricingLoader.StreamCsvRows(filePath).ToList());

        Assert.Equal(string.Empty, row["negotiated_rate"]);
    }

    [Fact]
    public void LoadCsv_MapsRaggedRowMissingFieldToEmptyString()
    {
        var filePath = Path.Combine(_tempDirectory, "load-ragged.csv");
        File.WriteAllText(filePath, "cpt_code,insurer,negotiated_rate\n70551,Plan A");

        var row = Assert.Single(PricingLoader.LoadCsv(filePath));

        Assert.Equal(string.Empty, row["negotiated_rate"]);
    }

    [Fact]
    public void StreamCsvRows_ThrowsForRaggedRowMissingField()
    {
        var filePath = Path.Combine(_tempDirectory, "stream-ragged.csv");
        File.WriteAllText(filePath, "cpt_code,insurer,negotiated_rate\n70551,Plan A");

        Assert.Throws<CsvHelper.MissingFieldException>(() => PricingLoader.StreamCsvRows(filePath).ToList());
    }

    [Fact]
    public void StreamCsvRows_EmptyFile_ReturnsNoRows()
    {
        var filePath = Path.Combine(_tempDirectory, "stream-empty.csv");
        File.WriteAllText(filePath, string.Empty);

        var rows = PricingLoader.StreamCsvRows(filePath).ToList();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task StreamJsonRowsAsync_ConvertsValuesAndSkipsNullRows()
    {
        var filePath = Path.Combine(_tempDirectory, "stream.json");
        await File.WriteAllTextAsync(filePath, """
        [
          null,
          {
            "cpt_code": "70551",
            "negotiated_rate": 1250.75,
            "cash_price": 950,
            "is_estimate": true
          }
        ]
        """);

        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in PricingLoader.StreamJsonRowsAsync(filePath, CancellationToken.None))
        {
            rows.Add(row);
        }

        Assert.Single(rows);
        Assert.Equal("70551", rows[0]["cpt_code"]);
        Assert.Equal("1250.75", rows[0]["negotiated_rate"]);
        Assert.Equal("950", rows[0]["cash_price"]);
        Assert.Equal("True", rows[0]["is_estimate"]);
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
