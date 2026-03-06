using HealthPilot.Api.DataPipeline;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HealthPilot.Api.Tests;

public class DataPipelineComponentsTests
{
    [Fact]
    public async Task StreamingParser_ParseAsync_ExtractsRequiredFields_FromCsvAndJson()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-data-pipeline-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var csvPath = Path.Combine(tempDirectory, "rates.csv");
            await File.WriteAllTextAsync(csvPath,
                "hospital_name,payer,cpt,procedure_description,negotiated_rate,cash_price,location\n" +
                "Metro Hospital,Acme Health,70450,CT HEAD,450.25,250.10,\"Seattle, WA\"");

            var jsonPath = Path.Combine(tempDirectory, "rates.json");
            await File.WriteAllTextAsync(jsonPath,
                """
                [
                  {
                    "provider_name": "Metro Hospital",
                    "insurer": "Acme Health",
                    "cpt_code": "70551",
                    "service_description": "MRI BRAIN",
                    "rate": 900.5,
                    "self_pay_price": 700,
                    "city": "Portland, OR"
                  }
                ]
                """);

            var parser = new StreamingParser();
            var csvRecords = await CollectAsync(parser.ParseAsync(csvPath));
            var jsonRecords = await CollectAsync(parser.ParseAsync(jsonPath));

            var csv = Assert.Single(csvRecords);
            Assert.Equal("Metro Hospital", csv.HospitalName);
            Assert.Equal("Acme Health", csv.Payer);
            Assert.Equal("70450", csv.ProcedureCode);
            Assert.Equal("CT HEAD", csv.ProcedureDescription);
            Assert.Equal(450.25m, csv.NegotiatedRate);
            Assert.Equal(250.10m, csv.CashPrice);
            Assert.Equal("Seattle, WA", csv.Location);

            var json = Assert.Single(jsonRecords);
            Assert.Equal("70551", json.ProcedureCode);
            Assert.Equal("MRI BRAIN", json.ProcedureDescription);
            Assert.Equal("Portland, OR", json.Location);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("70-450", "70450")]
    [InlineData("123", "00123")]
    [InlineData("a1234", "A1234")]
    [InlineData("", null)]
    public void Normalizer_NormalizeCptCode_StandardizesCodes(string raw, string? expected)
    {
        var normalized = Normalizer.NormalizeCptCode(raw);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public async Task Loader_LoadAsync_BatchUpsertsRates_AndCreatesSchema()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var loggerFactory = LoggerFactory.Create(builder => { });
        var loader = new Loader(loggerFactory.CreateLogger<Loader>());

        var records = GetRecords(
            new TransparencyRecord("Metro Hospital", "Acme Health", "70450", "CT HEAD", 450.25m, 250.10m, "Seattle, WA"),
            new TransparencyRecord("Metro Hospital", "Acme Health", "70450", "CT HEAD UPDATED", 470.00m, 260.00m, "Seattle, WA"));

        var loaded = await loader.LoadAsync(connection, records, batchSize: 1);

        Assert.Equal(2, loaded);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM providers;";
        var providers = Convert.ToInt32(await command.ExecuteScalarAsync());

        command.CommandText = "SELECT COUNT(*) FROM procedures;";
        var procedures = Convert.ToInt32(await command.ExecuteScalarAsync());

        command.CommandText = "SELECT COUNT(*) FROM rates;";
        var rates = Convert.ToInt32(await command.ExecuteScalarAsync());

        command.CommandText = "SELECT negotiated_rate, cash_price FROM rates LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var negotiatedRate = reader.GetDecimal(0);
        var cashPrice = reader.GetDecimal(1);

        Assert.Equal(1, providers);
        Assert.Equal(1, procedures);
        Assert.Equal(1, rates);
        Assert.Equal(470.00m, negotiatedRate);
        Assert.Equal(260.00m, cashPrice);
    }

    private static async IAsyncEnumerable<TransparencyRecord> GetRecords(params TransparencyRecord[] records)
    {
        foreach (var record in records)
        {
            yield return record;
            await Task.Yield();
        }
    }

    private static async Task<List<TransparencyRecord>> CollectAsync(IAsyncEnumerable<TransparencyRecord> records)
    {
        var output = new List<TransparencyRecord>();
        await foreach (var record in records)
        {
            output.Add(record);
        }

        return output;
    }
}
