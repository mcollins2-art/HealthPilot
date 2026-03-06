using HealthPilot.Api.DataPipeline;
using Xunit;

namespace HealthPilot.Api.Tests;

public class HospitalDataFilesTests
{
    [Fact]
    public async Task HospitalSampleFiles_ContainExpectedRecords()
    {
        var dataDirectory = Path.Combine(FindRepositoryRoot(), "data");
        var files = new[]
        {
            Path.Combine(dataDirectory, "mount_sinai_sample.csv"),
            Path.Combine(dataDirectory, "nyu_langone_sample.csv"),
            Path.Combine(dataDirectory, "hackensack_meridian_sample.csv")
        };

        foreach (var file in files)
        {
            Assert.True(File.Exists(file), $"Expected data file was not found: {file}");
        }

        var parser = new StreamingParser();
        var records = new List<TransparencyRecord>();

        foreach (var file in files)
        {
            await foreach (var record in parser.ParseAsync(file))
            {
                records.Add(record);
            }
        }

        Assert.Contains(records, record => record.HospitalName == "Mount Sinai Hospital" && record.ProcedureCode == "70450");
        Assert.Contains(records, record => record.HospitalName == "NYU Langone Hospitals" && record.ProcedureCode == "72148");
        Assert.Contains(records, record => record.HospitalName == "Hackensack University Medical Center" && record.ProcedureCode == "73721");
        Assert.Equal(6, records.Count);
        Assert.Equal(2, records.Count(record => record.HospitalName == "Mount Sinai Hospital"));
        Assert.Equal(2, records.Count(record => record.HospitalName == "NYU Langone Hospitals"));
        Assert.Equal(2, records.Count(record => record.HospitalName == "Hackensack University Medical Center"));
        Assert.All(records, record => Assert.True(record.NegotiatedRate.HasValue || record.CashPrice.HasValue));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HealthPilot.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing HealthPilot.sln.");
    }
}
