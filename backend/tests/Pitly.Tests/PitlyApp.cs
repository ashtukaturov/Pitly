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
/// The database is opened without connection pooling. xUnit runs test classes in parallel, and clearing the
/// process-wide SQLite pool (<c>SqliteConnection.ClearAllPools</c>) from one test's Dispose disposed the open
/// connections of other tests: their import then failed with an ObjectDisposedException (reported as HTTP 400)
/// or their host failed to start. Without pooling nothing stays open after a request, so the file can be deleted
/// without touching any other test's connections.
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

    private readonly Action<IServiceCollection>? _configureServices;

    public PitlyApp()
    {
    }

    /// <summary>Replaces services of the real application for one test (for example a service that fails).</summary>
    public PitlyApp(Action<IServiceCollection> configureServices)
    {
        _configureServices = configureServices;
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
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString(DatabasePath));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INbpExchangeRateService>();
            services.AddScoped<INbpExchangeRateService>(_ => new TestRateService(_rates));
            _configureServices?.Invoke(services);
        });
    }

    /// <summary>
    /// The connection string of a test database: no pooling, so that no test ever needs to clear the
    /// process-wide pool that the other tests running in parallel share.
    /// </summary>
    public static string ConnectionString(string path)
        => new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
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

        /// <summary>
        /// Fails unless the API answered 200 OK, with the API's own error body in the message: an unexpected
        /// rejection then says why, even when it happens once in a full parallel run and not when the test runs alone.
        /// </summary>
        public void AssertOk()
            => Assert.True(Status == HttpStatusCode.OK, $"Expected 200 OK, got {(int)Status} {Status}: {Body.GetRawText()}");
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
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            return new Response(response.StatusCode, document.RootElement.Clone());
        }
        catch (JsonException)
        {
            // A response that is not JSON (an unhandled error page) is kept as text, so that AssertOk can show it.
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { raw = text }));
            return new Response(response.StatusCode, document.RootElement.Clone());
        }
    }
}
