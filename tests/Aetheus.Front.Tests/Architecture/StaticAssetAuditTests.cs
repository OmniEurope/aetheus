// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Architecture;

public class StaticAssetAuditTests
{
    [Fact]
    public void UnusedBootstrapDistribution_IsNotPublished()
    {
        var root = FindRepoRoot();
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "lib", "bootstrap")));
    }

    [Fact]
    public void MonacoDiffEditor_DetachesModelsBeforeDisposingThem()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Aetheus.Front", "wwwroot", "js", "monaco-yaml.js"));
        var disposeStart = source.IndexOf("disposeDiffEditor: function", StringComparison.Ordinal);
        var detachIndex = source.IndexOf("entry.editor.setModel(null)", disposeStart, StringComparison.Ordinal);
        var editorDisposeIndex = source.IndexOf("entry.editor.dispose()", disposeStart, StringComparison.Ordinal);
        var originalModelDisposeIndex = source.IndexOf("entry.originalModel.dispose()", disposeStart, StringComparison.Ordinal);
        var modifiedModelDisposeIndex = source.IndexOf("entry.modifiedModel.dispose()", disposeStart, StringComparison.Ordinal);

        Assert.True(disposeStart >= 0);
        Assert.True(detachIndex > disposeStart);
        Assert.True(editorDisposeIndex > detachIndex);
        Assert.True(originalModelDisposeIndex > editorDisposeIndex);
        Assert.True(modifiedModelDisposeIndex > originalModelDisposeIndex);
    }

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
