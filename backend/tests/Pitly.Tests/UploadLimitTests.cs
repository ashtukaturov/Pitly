using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pitly.Api.Services;

namespace Pitly.Tests;

public class UploadLimitTests
{
    private const string Aapl = "US0378331005";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root with docker-compose.yml not found.");
    }

    [Theory]
    [InlineData("frontend/nginx.conf")]          // image built by docker-compose.yml (local use)
    [InlineData("docker/frontend.nginx.conf")]   // image built for the hosted deployment
    public void Nginx_AllowsTheSameRequestSizeAsTheApi(string relativePath)
    {
        var config = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

        var match = Regex.Match(config, @"client_max_body_size\s+(\d+)m\s*;");
        Assert.True(match.Success, $"{relativePath} must set client_max_body_size: the Nginx default of 1m rejects ordinary statements.");
        Assert.Equal(UploadLimits.MaxRequestBytes, long.Parse(match.Groups[1].Value) * 1024 * 1024);
        Assert.True(UploadLimits.MaxFileBytes <= UploadLimits.MaxRequestBytes);
        Assert.Equal(10L * 1024 * 1024, UploadLimits.MaxFileBytes);
    }

    [Fact]
    public void DockerCompose_PublishesPortsOnLoopbackOnly()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "docker-compose.yml"));

        var ports = Regex.Matches(compose, "-\\s*\"([^\"]+:\\d+:\\d+|\\d+:\\d+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(ports);
        Assert.All(ports, port => Assert.StartsWith("127.0.0.1:", port));
    }

    [Fact]
    public async Task Import_AcceptsAValidTwoMebibyteStatement()
    {
        var statement = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-01-10, 10:00:00", 1m, 25m)
            .Sell("AAPL", "2025-06-10, 10:00:00", 1m, 30m)
            .PadTo(2 * 1024 * 1024)
            .Build();
        Assert.True(Encoding.UTF8.GetByteCount(statement) >= 2 * 1024 * 1024);

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("two-mebibytes.csv", statement)]);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal(20m, result.Field("dochod"));
    }

    [Fact]
    public async Task Import_RejectsAFileAboveTenMebibytesWithAMachineReadableCode()
    {
        var statement = new IbStatement("U1111111", 2025).Instrument("AAPL", Aapl)
            .Buy("AAPL", "2025-01-10, 10:00:00", 1m, 25m)
            .PadTo(10 * 1024 * 1024 + 1024)
            .Build();

        using var app = new PitlyApp();
        var result = await app.ImportAsync([("huge.csv", statement)]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, result.Status);
        Assert.Equal("file_too_large", result.Body.GetProperty("code").GetString());
        Assert.Equal("huge.csv", result.Body.GetProperty("fileName").GetString());
        Assert.Equal(10, result.Body.GetProperty("maxFileMb").GetInt32());
        Assert.Equal(64, result.Body.GetProperty("maxRequestMb").GetInt32());
    }
}
