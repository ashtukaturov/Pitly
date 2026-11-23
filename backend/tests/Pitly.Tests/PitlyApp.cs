using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pitly.Core.Services;

namespace Pitly.Tests;

/// <summary>
/// The real API (Program.cs: endpoints, parsers, calculators, SQLite, schema upgrade) hosted in-process for
/// end-to-end tests. Only two things are replaced so that results are reproducible without a network:
/// the NBP exchange rates are fixed (USD = 4 PLN, EUR = 4.50 PLN) and the database is a temporary file.
/// </summary>
internal sealed class PitlyApp : WebApplicationFactory<Program>
{
    public const decimal UsdRate = 4m;

    private readonly Dictionary<string, decimal> _rates = new()
    {
        ["USD"] = UsdRate,
        ["EUR"] = 4.5m,
        ["PLN"] = 1m
    };

    public PitlyApp()
    {
    }

    /// <summary>Extra fixed rates for a test; a key such as "EUR@2025-01-31" sets the rate of one day.</summary>
    public PitlyApp(IReadOnlyDictionary<string, decimal> extraRates)
    {
        foreach (var (key, value) in extraRates)
            _rates[key] = value;
    }

    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), "pitly-tests-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Data Source=" + DatabasePath);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INbpExchangeRateService>();
            services.AddScoped<INbpExchangeRateService>(_ => new TestRateService(_rates));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { DatabasePath, DatabasePath + "-shm", DatabasePath + "-wal" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Left for the OS temp cleanup; never fails a test.
            }
        }
    }

    public sealed record Response(HttpStatusCode Status, JsonElement Body)
    {
        public JsonElement Pit38 => Body.GetProperty("pit38");
        public JsonElement Summary => Body.GetProperty("summary");
        public string Error => Body.GetProperty("error").GetString() ?? string.Empty;
        public decimal Field(string name) => Pit38.GetProperty(name).GetDecimal();
        public List<string> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
    }

    /// <summary>Uploads CSV files through POST /api/import exactly as the browser does (multipart/form-data).</summary>
    public async Task<Response> ImportAsync(
        IEnumerable<(string Name, string Csv)> files,
        int? taxYear = null,
        string? residencyStartDate = null,
        string? priorYearLossesJson = null,
        string? accountLabelsJson = null,
        string? creditMethod = null)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, csv) in files)
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
            content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            form.Add(content, "files", name);
        }

        if (taxYear is not null) form.Add(new StringContent(taxYear.Value.ToString()), "taxYear");
        if (residencyStartDate is not null) form.Add(new StringContent(residencyStartDate), "residencyStartDate");
        if (priorYearLossesJson is not null) form.Add(new StringContent(priorYearLossesJson), "priorYearLosses");
        if (accountLabelsJson is not null) form.Add(new StringContent(accountLabelsJson), "accountLabels");
        if (creditMethod is not null) form.Add(new StringContent(creditMethod), "creditMethod");

        using var client = CreateClient();
        using var response = await client.PostAsync("/api/import", form);
        return await ReadAsync(response);
    }

    public async Task<Response> GetAsync(string url)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(url);
        return await ReadAsync(response);
    }

    private static async Task<Response> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return new Response(response.StatusCode, document.RootElement.Clone());
    }
}
