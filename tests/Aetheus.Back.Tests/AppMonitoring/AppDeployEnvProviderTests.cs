// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AppMonitoring;
using Aetheus.Back.Components.AppMonitoring.Ingest;
using Aetheus.Back.Components.Auth;
using Aetheus.Back.Data;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Aetheus.Back.Tests.AppMonitoring;

public class AppDeployEnvProviderTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ISecretMaskingService _masking = Substitute.For<ISecretMaskingService>();
    private readonly IngestKeyHasher _hasher =
        new(Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-least-32-bytes!!" }));

    public AppDeployEnvProviderTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _db.MonitoredApps.Add(new MonitoredApp { Id = 1, ProjectId = 1, Name = "app", Enabled = true });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private AppDeployEnvProvider Build(string? baseUrl)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AppMonitoring:IngestBaseUrl"] = baseUrl
        }).Build();
        return new AppDeployEnvProvider(new AppMonitoringRepository(_db), _hasher, _masking, config,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task LinkedApp_WithBaseUrl_ReturnsOtelEnv_AndRotatesKey()
    {
        var env = await Build("https://obs.example.com").GetDeployEnvAsync(1, null, pipelineRunId: 7, ct: TestContext.Current.CancellationToken);

        Assert.Equal("https://obs.example.com/api/ingest/otlp/v1", env["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("http/json", env["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.StartsWith("x-aetheus-ingest-key=", env["OTEL_EXPORTER_OTLP_HEADERS"]);

        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(app.IngestKeyHash); // a fresh key was issued and stored (hashed)

        // The plaintext key is registered for masking in the run's logs.
        _masking.Received(1).RegisterRuntimeSecret(7, Arg.Any<string>());
    }

    [Fact]
    public async Task NoBaseUrl_ReturnsEmpty_AndDoesNotTouchApp()
    {
        var env = await Build(baseUrl: null).GetDeployEnvAsync(1, null, pipelineRunId: 7, ct: TestContext.Current.CancellationToken);
        Assert.Empty(env);
        var app = await _db.MonitoredApps.AsNoTracking().SingleAsync(a => a.Id == 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(app.IngestKeyHash);
        _masking.DidNotReceive().RegisterRuntimeSecret(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task NoLinkedApp_ReturnsEmpty()
    {
        var env = await Build("https://obs.example.com").GetDeployEnvAsync(projectId: 999, null, pipelineRunId: 7, ct: TestContext.Current.CancellationToken);
        Assert.Empty(env);
    }
}
