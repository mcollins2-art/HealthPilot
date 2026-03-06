using HealthPilot.Api.DataPipeline;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HealthPilot.Api.Tests;

public class HospitalTransparencyIngestionWorkflowTests
{
    [Fact]
    public async Task LinkDiscoverer_DiscoverAsync_FindsCsvAndJsonLinks()
    {
        var page = new Uri("https://example.org/transparency");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == page)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        <html>
                          <body>
                            <a href="/files/rates.csv">CSV</a>
                            <a href="https://cdn.example.org/pricing/negotiated.json">JSON</a>
                            <a href="/files/ignore.pdf">PDF</a>
                          </body>
                        </html>
                        """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }));

        var logger = LoggerFactory.Create(builder => { }).CreateLogger<TransparencyMachineReadableLinkDiscoverer>();
        var discoverer = new TransparencyMachineReadableLinkDiscoverer(httpClient, logger);

        var links = await discoverer.DiscoverAsync(page);

        Assert.Equal(2, links.Count);
        Assert.Contains(new Uri("https://example.org/files/rates.csv"), links);
        Assert.Contains(new Uri("https://cdn.example.org/pricing/negotiated.json"), links);
    }

    [Fact]
    public async Task Workflow_DownloadAndParseCptRatesAsync_DownloadsFilesAndExtractsCptRates()
    {
        var transparencyPage = new Uri("https://example.org/transparency");
        var csvFile = new Uri("https://example.org/files/rates.csv");
        var jsonFile = new Uri("https://example.org/files/rates.json");

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == transparencyPage)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        <html>
                          <body>
                            <a href="/files/rates.csv">CSV</a>
                            <a href="/files/rates.json">JSON</a>
                          </body>
                        </html>
                        """)
                };
            }

            if (request.RequestUri == csvFile)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        hospital_name,payer,billing_code,description,negotiated_dollar,standard_charge_discounted_cash,location
                        Metro Hospital,Acme Health,70-450,CT HEAD,450.25,250.10,"Seattle, WA"
                        Metro Hospital,Acme Health,N/A,NON CPT,50,25,"Seattle, WA"
                        """)
                };
            }

            if (request.RequestUri == jsonFile)
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        [
                          {
                            "provider_name": "Metro Hospital",
                            "insurer": "Acme Health",
                            "cpt_hcpcs_code": "70551",
                            "service_description": "MRI BRAIN",
                            "standard_charge_negotiated_dollar": 900.5,
                            "discounted_cash_price": 700
                          },
                          {
                            "provider_name": "Metro Hospital",
                            "insurer": "Acme Health",
                            "billing_code": "DRG123",
                            "standard_charge_negotiated_dollar": 100
                          }
                        ]
                        """)
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        }));

        var loggerFactory = LoggerFactory.Create(builder => { });
        var discoverer = new TransparencyMachineReadableLinkDiscoverer(httpClient, loggerFactory.CreateLogger<TransparencyMachineReadableLinkDiscoverer>());
        var downloader = new Downloader(httpClient, loggerFactory.CreateLogger<Downloader>());
        var parser = new StreamingParser();
        var workflow = new HospitalTransparencyIngestionWorkflow(discoverer, downloader, parser, loggerFactory.CreateLogger<HospitalTransparencyIngestionWorkflow>());

        var tempDirectory = Path.Combine(Path.GetTempPath(), "healthpilot-transparency-downloads", Guid.NewGuid().ToString("N"));

        try
        {
            var records = await workflow.DownloadAndParseCptRatesAsync([transparencyPage], tempDirectory);

            Assert.Equal(2, records.Count);
            Assert.DoesNotContain(records, record => record.ProcedureCode == "70-450");
            Assert.DoesNotContain(records, record => string.Equals(record.ProcedureCode, "N/A", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(records, record => record.ProcedureCode == "70450" && record.NegotiatedRate == 450.25m && record.CashPrice == 250.10m);
            Assert.Contains(records, record => record.ProcedureCode == "70551" && record.NegotiatedRate == 900.5m && record.CashPrice == 700m);

            Assert.True(File.Exists(Path.Combine(tempDirectory, "rates.csv")));
            Assert.True(File.Exists(Path.Combine(tempDirectory, "rates.json")));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }
}
