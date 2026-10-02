// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Components.Servers;

namespace Aetheus.Front.Tests.Pages.Servers;

public class AgentUpdateProgressCardTests
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type CardType = typeof(AgentUpdateProgressCard);

    // === PhaseIcon (private static) ===

    [Theory]
    [InlineData(AgentUpdatePhase.Queued, "schedule")]
    [InlineData(AgentUpdatePhase.PickedUp, "play_arrow")]
    [InlineData(AgentUpdatePhase.Downloading, "cloud_download")]
    [InlineData(AgentUpdatePhase.Downloaded, "task")]
    [InlineData(AgentUpdatePhase.Extracting, "folder_zip")]
    [InlineData(AgentUpdatePhase.LaunchingUpdater, "rocket_launch")]
    [InlineData(AgentUpdatePhase.AgentOffline, "hourglass_top")]
    [InlineData(AgentUpdatePhase.Done, "check_circle")]
    [InlineData(AgentUpdatePhase.Failed, "error")]
    public void PhaseIcon_ReturnsExpectedIcon(AgentUpdatePhase phase, string expected)
    {
        var method = CardType.GetMethod("PhaseIcon", PrivStatic)!;
        var result = (string)method.Invoke(null, [phase])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PhaseIcon_Null_ReturnsSchedule()
    {
        var method = CardType.GetMethod("PhaseIcon", PrivStatic)!;
        var result = (string)method.Invoke(null, [null])!;
        Assert.Equal("schedule", result);
    }

    // === PhaseIconClass (private static) ===

    [Theory]
    [InlineData(AgentUpdatePhase.Done, "agent-update-phase-done")]
    [InlineData(AgentUpdatePhase.Failed, "agent-update-phase-failed")]
    [InlineData(AgentUpdatePhase.Queued, "agent-update-phase-active")]
    [InlineData(AgentUpdatePhase.Downloading, "agent-update-phase-active")]
    [InlineData(AgentUpdatePhase.AgentOffline, "agent-update-phase-active")]
    public void PhaseIconClass_ReturnsExpected(AgentUpdatePhase phase, string expected)
    {
        var method = CardType.GetMethod("PhaseIconClass", PrivStatic)!;
        var result = (string)method.Invoke(null, [phase])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PhaseIconClass_Null_ReturnsActive()
    {
        var method = CardType.GetMethod("PhaseIconClass", PrivStatic)!;
        var result = (string)method.Invoke(null, [null])!;
        Assert.Equal("agent-update-phase-active", result);
    }

    // === PhaseBadge (private static) ===

    [Theory]
    [InlineData(AgentUpdatePhase.Done, OmniTone.Success)]
    [InlineData(AgentUpdatePhase.Failed, OmniTone.Danger)]
    [InlineData(AgentUpdatePhase.AgentOffline, OmniTone.Warning)]
    [InlineData(AgentUpdatePhase.Queued, OmniTone.Accent)]
    [InlineData(AgentUpdatePhase.Downloading, OmniTone.Accent)]
    [InlineData(AgentUpdatePhase.PickedUp, OmniTone.Accent)]
    [InlineData(AgentUpdatePhase.LaunchingUpdater, OmniTone.Accent)]
    public void PhaseBadge_ReturnsExpected(AgentUpdatePhase phase, OmniTone expected)
    {
        var method = CardType.GetMethod("PhaseBadge", PrivStatic)!;
        var result = (OmniTone)method.Invoke(null, [phase])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PhaseBadge_Null_ReturnsInfo()
    {
        var method = CardType.GetMethod("PhaseBadge", PrivStatic)!;
        var result = (OmniTone)method.Invoke(null, [null])!;
        Assert.Equal(OmniTone.Accent, result);
    }

    // === PhaseLabel (private instance - uses localizer so test the key mapping) ===

    [Theory]
    [InlineData(AgentUpdatePhase.Queued, "AgentUpdatePhase_Queued")]
    [InlineData(AgentUpdatePhase.PickedUp, "AgentUpdatePhase_PickedUp")]
    [InlineData(AgentUpdatePhase.Downloading, "AgentUpdatePhase_Downloading")]
    [InlineData(AgentUpdatePhase.Downloaded, "AgentUpdatePhase_Downloaded")]
    [InlineData(AgentUpdatePhase.Extracting, "AgentUpdatePhase_Extracting")]
    [InlineData(AgentUpdatePhase.LaunchingUpdater, "AgentUpdatePhase_LaunchingUpdater")]
    [InlineData(AgentUpdatePhase.AgentOffline, "AgentUpdatePhase_AgentOffline")]
    [InlineData(AgentUpdatePhase.Done, "AgentUpdatePhase_Done")]
    [InlineData(AgentUpdatePhase.Failed, "AgentUpdatePhase_Failed")]
    public void PhaseLabel_ReturnsExpectedKey(AgentUpdatePhase phase, string expectedKey)
    {
        var instance = CreateInstance();
        var method = CardType.GetMethod("PhaseLabel", Priv)!;
        var result = (string)method.Invoke(instance, [phase])!;
        // The method returns the L["key"] localizer lookup result; in test the localizer
        // stub returns the key itself (BunitTestHelper pattern).
        Assert.Contains(expectedKey, result);
    }

    [Fact]
    public void PhaseLabel_Null_ReturnsQueuedKey()
    {
        var instance = CreateInstance();
        var method = CardType.GetMethod("PhaseLabel", Priv)!;
        var result = (string)method.Invoke(instance, [null])!;
        Assert.Contains("AgentUpdatePhase_Queued", result);
    }

    // === HandleHeartbeat ===

    [Fact]
    public void HandleHeartbeat_NotInUpdatePhase_LeavesItNull()
    {
        var instance = CreateInstance();
        SetField(instance, "_phase", null);

        instance.HandleHeartbeat("2.0.0");

        Assert.Null(GetField<AgentUpdatePhase?>(instance, "_phase"));
    }

    [Fact]
    public void HandleHeartbeat_SameVersion_DoesNotConfirmUpdate()
    {
        var instance = CreateInstance();
        SetField(instance, "_phase", AgentUpdatePhase.AgentOffline);

        instance.HandleHeartbeat("1.0.0");

        Assert.Equal(AgentUpdatePhase.AgentOffline, GetField<AgentUpdatePhase?>(instance, "_phase"));
    }

    [Fact]
    public void HandleConfirmed_TransitionsToDone()
    {
        var instance = CreateInstance();
        SetField(instance, "_phase", AgentUpdatePhase.AgentOffline);

        try { instance.HandleConfirmed(42, "2.0.0"); }
        catch (InvalidOperationException) { }

        Assert.Equal(AgentUpdatePhase.Done, GetField<AgentUpdatePhase?>(instance, "_phase"));
        Assert.Equal(100, GetField<int>(instance, "_percent"));
        Assert.Equal(42, GetField<int?>(instance, "_requestId"));
    }

    // === DisposeAsync ===

    [Fact]
    public async Task DisposeAsync_DisposesHandlers()
    {
        var instance = CreateInstance();
        await instance.DisposeAsync();
        Assert.Null(GetField<System.Threading.Timer?>(instance, "_staleTimer"));
    }

    // === Helpers ===

    private static AgentUpdateProgressCard CreateInstance()
    {
        var instance = new AgentUpdateProgressCard();
        // Wire up the localizer stub used by PhaseLabel
        var localizer = new BunitTestHelper.StubLocalizer();
        CardType.GetProperty("L", Priv)!.SetValue(instance, localizer);
        CardType.GetProperty("Toast", Priv)!.SetValue(instance,
            new Aetheus.Front.Components.Shared.NotifyHelper(new OmniEurope.Blazor.Components.OmniOverlayService(new Microsoft.Extensions.Time.Testing.FakeTimeProvider()), localizer));
        return instance;
    }

    private static void SetField(object instance, string name, object? value)
        => CardType.GetField(name, Priv)!.SetValue(instance, value);

    private static T GetField<T>(object instance, string name)
        => (T)CardType.GetField(name, Priv)!.GetValue(instance)!;
}
