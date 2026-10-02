// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.AgentUpdate;
using Aetheus.Back.Components.Audit;
using Aetheus.Back.Components.Servers;
using Aetheus.Back.Data.Entities;
using Aetheus.Back.Hubs;
using Aetheus.Back.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Aetheus.Back.Tests.Servers;

/// <summary>
/// Recette R2-023: the sudoers drift alert fired on every heartbeat, forever, once an agent update had
/// legitimately re-rendered the drop-ins. The baseline is now re-captured by the heartbeat that confirms an
/// agent update, and a drifted state is alerted once (fingerprint persisted on the server row).
/// </summary>
public sealed class ServerHeartbeatSudoersDriftTests
{
    private const string Agent = "aetheus-agent";
    private const string Apache = "aetheus-apache";
    private static readonly string HashA = new('A', 64);
    private static readonly string HashB = new('B', 64);
    private static readonly string HashC = new('C', 64);
    private static readonly string HashD = new('D', 64);

    private readonly IServerRepository _repo = Substitute.For<IServerRepository>();
    private readonly IHubClients _alertClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _alertProxy = Substitute.For<IClientProxy>();
    private readonly IAgentUpdateConfirmationService _confirmation = Substitute.For<IAgentUpdateConfirmationService>();
    private readonly Server _server = new() { Id = 7, Name = "web-1", Hostname = "web-1" };

    public ServerHeartbeatSudoersDriftTests()
    {
        _repo.FindServerAsync(_server.Id, Arg.Any<CancellationToken>()).Returns(_server);
        _alertClients.Group(HubGroups.Alerts).Returns(_alertProxy);
        _confirmation.ProcessHeartbeatAsync(_server.Id, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
    }

    // A fresh service per heartbeat: the only state carried between calls is the server row, exactly as
    // across backend restarts.
    private ServerService CreateService()
    {
        var hub = Substitute.For<IHubContext<ServerHub>>();
        hub.Clients.Returns(Substitute.For<IHubClients>());
        var alertHub = Substitute.For<IHubContext<AlertHub>>();
        alertHub.Clients.Returns(_alertClients);
        return new ServerService(_repo, Substitute.For<IServerHeartbeatRepository>(), hub, alertHub,
            Substitute.For<IAuditService>(), Substitute.For<Aetheus.Back.Components.Tasks.ITaskService>(),
            Options.Create(new BackgroundServicesOptions()), TimeProvider.System,
            Substitute.For<IDbTransactionScope>(), updateConfirmation: _confirmation);
    }

    private Task HeartbeatAsync(params (string File, string Hash)[] hashes) =>
        CreateService().ProcessHeartbeatAsync(_server.Id, new ServerHeartbeatDto
        {
            AgentVersion = "1.0.0",
            SudoersInventoryAvailable = true,
            SudoersHashes = hashes.ToDictionary(item => item.File, item => item.Hash)
        }, TestContext.Current.CancellationToken);

    private void ConfirmNextHeartbeatAsUpdateTo(string version) =>
        _confirmation.ProcessHeartbeatAsync(_server.Id, Arg.Any<ServerHeartbeatDto>(), Arg.Any<CancellationToken>())
            .Returns(version, (string?)null);

    private List<AlertTriggeredDto> DriftAlerts() => _alertProxy.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(IClientProxy.SendCoreAsync))
        .Select(call => call.GetArguments())
        .Where(arguments => Equals(arguments[0], "AlertTriggered"))
        .Select(arguments => ((object?[])arguments[1]!)[0])
        .OfType<AlertTriggeredDto>()
        .Where(alert => alert.Metric == "SudoersDrift")
        .ToList();

    [Fact]
    public async Task FirstReport_CapturesTheBaselineWithoutAlert()
    {
        await HeartbeatAsync((Agent, HashA), (Apache, HashB));

        Assert.Contains(HashA, _server.SudoersBaseline);
        Assert.Contains(HashB, _server.SudoersBaseline);
        Assert.Null(_server.SudoersDriftAlertedFingerprint);
        Assert.Empty(DriftAlerts());
    }

    [Fact]
    public async Task ConfirmedAgentUpdate_RecapturesTheBaselineWithoutAlert()
    {
        await HeartbeatAsync((Agent, HashA), (Apache, HashB));

        ConfirmNextHeartbeatAsUpdateTo("2.0.0");
        await HeartbeatAsync((Agent, HashC), (Apache, HashD));
        await HeartbeatAsync((Agent, HashC), (Apache, HashD));

        Assert.Contains(HashC, _server.SudoersBaseline);
        Assert.Contains(HashD, _server.SudoersBaseline);
        Assert.DoesNotContain(HashA, _server.SudoersBaseline);
        Assert.Null(_server.SudoersDriftAlertedFingerprint);
        Assert.Empty(DriftAlerts());
    }

    [Fact]
    public async Task ConfirmedAgentUpdateWithoutHashes_TheNextReportBecomesTheBaseline()
    {
        await HeartbeatAsync((Agent, HashA));

        ConfirmNextHeartbeatAsUpdateTo("2.0.0");
        await HeartbeatAsync();
        Assert.Null(_server.SudoersBaseline);

        await HeartbeatAsync((Agent, HashC));

        Assert.Contains(HashC, _server.SudoersBaseline);
        Assert.Empty(DriftAlerts());
    }

    [Fact]
    public async Task GenuineDrift_AlertsOnce_NotOnTheNextHeartbeatNorAfterARestart()
    {
        await HeartbeatAsync((Agent, HashA), (Apache, HashB));

        await HeartbeatAsync((Agent, HashC), (Apache, HashD));
        await HeartbeatAsync((Agent, HashC), (Apache, HashD));
        await HeartbeatAsync((Agent, HashC), (Apache, HashD));

        var alert = Assert.Single(DriftAlerts());
        Assert.Equal("Critical", alert.Severity);
        Assert.Equal(_server.Id, alert.ServerId);
        Assert.Contains("aetheus-agent, aetheus-apache", alert.Message, StringComparison.Ordinal);
        Assert.NotNull(_server.SudoersDriftAlertedFingerprint);
        Assert.Contains(HashA, _server.SudoersBaseline);
    }

    [Fact]
    public async Task SecondDifferentDrift_AlertsAgain()
    {
        await HeartbeatAsync((Agent, HashA), (Apache, HashB));

        await HeartbeatAsync((Agent, HashC), (Apache, HashB));
        await HeartbeatAsync((Agent, HashC), (Apache, HashB));
        await HeartbeatAsync((Agent, HashC), (Apache, HashD));
        await HeartbeatAsync((Agent, HashC), (Apache, HashD));

        var alerts = DriftAlerts();
        Assert.Equal(2, alerts.Count);
        Assert.Contains("changed out-of-band: aetheus-agent. ", alerts[0].Message, StringComparison.Ordinal);
        Assert.Contains("changed out-of-band: aetheus-agent, aetheus-apache. ", alerts[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnToBaseline_ClearsTheAlertedState_SoALaterDriftAlertsAgain()
    {
        await HeartbeatAsync((Agent, HashA));
        await HeartbeatAsync((Agent, HashC));
        Assert.NotNull(_server.SudoersDriftAlertedFingerprint);

        await HeartbeatAsync((Agent, HashA));
        Assert.Null(_server.SudoersDriftAlertedFingerprint);

        await HeartbeatAsync((Agent, HashC));

        Assert.Equal(2, DriftAlerts().Count);
    }
}
