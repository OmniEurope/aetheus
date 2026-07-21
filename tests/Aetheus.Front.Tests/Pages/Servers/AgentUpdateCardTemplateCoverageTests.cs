// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.Servers;
using Aetheus.Shared.Enums;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

/// <summary>
/// Render-level tests for AgentUpdateProgressCard template branches.
/// The card is invisible at rest (_phase == null). We set _phase via reflection
/// to exercise each visible branch without needing a real SignalR hub.
/// </summary>
public class AgentUpdateCardTemplateCoverageTests : BunitContext
{
    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Type CardType = typeof(AgentUpdateProgressCard);

    public AgentUpdateCardTemplateCoverageTests()
    {
        BunitTestHelper.RegisterServices(this);
    }

    [Fact]
    public void Renders_Invisible_ByDefault()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        // When _phase is null and _showDoneUntil is in the past, the card content is absent
        Assert.DoesNotContain("agent-update-progress-card", cut.Markup);
    }

    [Fact]
    public void Renders_Visible_WhenPhaseIsQueued()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        SetPhase(cut.Instance, AgentUpdatePhase.Queued, 0, null);
        cut.Render();
        Assert.Contains("agent-update-progress-card", cut.Markup);
    }

    [Fact]
    public void Renders_FailedBranch_ShowsFailedText()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        SetPhase(cut.Instance, AgentUpdatePhase.Failed, 0, "Something went wrong");
        cut.Render();
        Assert.Contains("agent-update-progress-card", cut.Markup);
    }

    [Fact]
    public void Renders_InProgressBranch_ShowsProgressText()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        SetPhase(cut.Instance, AgentUpdatePhase.Downloading, 40, "Downloading update...");
        cut.Render();
        Assert.Contains("agent-update-progress-card", cut.Markup);
        // Message should appear when non-null/whitespace
        Assert.Contains("Downloading update...", cut.Markup);
    }

    [Fact]
    public void Renders_NoMessage_WhenMessageIsNull()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        SetPhase(cut.Instance, AgentUpdatePhase.PickedUp, 10, null);
        cut.Render();
        Assert.Contains("agent-update-progress-card", cut.Markup);
    }

    [Fact]
    public void Renders_AgentOffline_IndeterminateMode()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        SetPhase(cut.Instance, AgentUpdatePhase.AgentOffline, 95, "Restarting agent...");
        cut.Render();
        Assert.Contains("agent-update-progress-card", cut.Markup);
    }

    [Fact]
    public void Renders_Done_WhenShowDoneUntilInFuture()
    {
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        // Set _phase = Done but _showDoneUntil to future so card stays visible
        CardType.GetField("_phase", Priv)!.SetValue(cut.Instance, AgentUpdatePhase.Done);
        CardType.GetField("_percent", Priv)!.SetValue(cut.Instance, 100);
        CardType.GetField("_showDoneUntil", Priv)!.SetValue(cut.Instance, DateTime.Now.AddSeconds(10));
        cut.Render();
        Assert.Contains("agent-update-progress-card", cut.Markup);
    }

    [Fact]
    public void OnParametersSet_WithNullHub_DoesNotThrow()
    {
        // With a null hub there is nothing to subscribe to, so _phase stays null and the
        // card renders its invisible (empty) branch instead of throwing.
        var cut = Render<AgentUpdateProgressCard>(p =>
        {
            p.Add(x => x.ServerId, 1);
            p.Add(x => x.Hub, null);
        });
        Assert.Null(CardType.GetField("_phase", Priv)!.GetValue(cut.Instance));
        Assert.DoesNotContain("agent-update-progress-card", cut.Markup);
    }

    private static void SetPhase(AgentUpdateProgressCard instance, AgentUpdatePhase phase, int percent, string? message)
    {
        CardType.GetField("_phase", Priv)!.SetValue(instance, phase);
        CardType.GetField("_percent", Priv)!.SetValue(instance, percent);
        CardType.GetField("_message", Priv)!.SetValue(instance, message);
    }
}
