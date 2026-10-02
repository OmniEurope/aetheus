// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.Pipelines;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Aetheus.Front.Tests.Pages.Pipelines;

/// <summary>A reader who scrolls a live run's logs up by hand is not dragged back to the newest line.</summary>
public sealed class PipelineRunLogFollowTests : BunitContext
{
    private static readonly ElementReference Terminal = new("terminal-1");

    public PipelineRunLogFollowTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private int Pins() => JSInterop.Invocations.Count(call => call.Identifier == "Aetheus.scrollToBottom");

    [Fact]
    public async Task ScrollingUpByHand_StopsPinning_AndReachingTheTailAgainResumesIt()
    {
        var changes = 0;
        using var follow = new PipelineRunLogFollow(JSInterop.JSRuntime, () => { changes++; return Task.CompletedTask; });

        await follow.AfterRenderAsync(Terminal, 10);
        Assert.Equal(1, Pins());
        Assert.Single(JSInterop.Invocations, call => call.Identifier == "Aetheus.watchLogFollow");

        await follow.OnUserScrolled(atBottom: false);
        await follow.AfterRenderAsync(Terminal, 20);
        await follow.AfterRenderAsync(Terminal, 30);
        Assert.False(follow.Following);
        Assert.Equal(1, Pins());

        await follow.OnUserScrolled(atBottom: true);
        await follow.AfterRenderAsync(Terminal, 40);
        Assert.True(follow.Following);
        Assert.Equal(2, Pins());
        Assert.Equal(2, changes);
        // The terminal is hooked once, not on every render.
        Assert.Single(JSInterop.Invocations, call => call.Identifier == "Aetheus.watchLogFollow");
    }
}
