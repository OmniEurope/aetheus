// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages;
using Bunit;

namespace Aetheus.Front.Tests.Pages;

public class MonacoDiffViewerTests : BunitContext
{
    public MonacoDiffViewerTests()
    {
        BunitTestHelper.RegisterServices(this);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Renders_ContainerDiv()
    {
        var cut = Render<MonacoDiffViewer>(p => p
            .Add(x => x.OriginalValue, "old content")
            .Add(x => x.ModifiedValue, "new content")
            .Add(x => x.IsDark, true));

        Assert.Contains("monaco-diff-", cut.Markup);
    }

    [Fact]
    public void Renders_WithEmptyValues()
    {
        var cut = Render<MonacoDiffViewer>(p => p
            .Add(x => x.OriginalValue, "")
            .Add(x => x.ModifiedValue, ""));

        // Even with empty values the diff container element is rendered.
        Assert.Contains("monaco-diff-", cut.Markup);
    }

    [Fact]
    public void CallsJsInterop_OnFirstRender()
    {
        var cut = Render<MonacoDiffViewer>(p => p
            .Add(x => x.OriginalValue, "a")
            .Add(x => x.ModifiedValue, "b")
            .Add(x => x.IsDark, false));

        var invocations = JSInterop.Invocations;
        Assert.Contains(invocations, i => i.Identifier == "monacoInterop.initDiffEditor");
    }

    [Fact]
    public void LightMode_PassesFalseToJs()
    {
        var cut = Render<MonacoDiffViewer>(p => p
            .Add(x => x.OriginalValue, "x")
            .Add(x => x.ModifiedValue, "y")
            .Add(x => x.IsDark, false));

        var init = JSInterop.Invocations
            .FirstOrDefault(i => i.Identifier == "monacoInterop.initDiffEditor");
        Assert.Equal(false, init.Arguments[3]);
    }

    [Fact]
    public async Task DisposeAsync_CallsJsDispose()
    {
        var cut = Render<MonacoDiffViewer>(p => p
            .Add(x => x.OriginalValue, "a")
            .Add(x => x.ModifiedValue, "b"));

        await cut.Instance.DisposeAsync();

        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "monacoInterop.disposeDiffEditor");
    }
}
