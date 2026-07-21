// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Git;

namespace Aetheus.Back.Tests.Git;

public class GitUnifiedDiffParserTests
{
    [Theory]
    [InlineData("a1b2c3d", true)]
    [InlineData("4b825dc642cb6eb9a060e54bf8d69288fbee4904", true)]
    [InlineData("abc", false)]          // too short
    [InlineData("z1b2c3d", false)]      // non-hex
    [InlineData("A1B2C3D", false)]      // uppercase (git emits lowercase)
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsSha_ValidatesHex(string? sha, bool expected) =>
        Assert.Equal(expected, GitUnifiedDiffParser.IsSha(sha));

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        var diff = GitUnifiedDiffParser.Parse("");
        Assert.Empty(diff.FileDiffs);
        Assert.Equal(0, diff.Stats.FilesChanged);
    }

    [Fact]
    public void Parse_ModifiedFile_CountsAddsAndDeletes()
    {
        const string patch = """
            diff --git a/foo.txt b/foo.txt
            index e69de29..4b825dc 100644
            --- a/foo.txt
            +++ b/foo.txt
            @@ -1,2 +1,3 @@
             line1
            -old
            +new
            +added
            """;

        var diff = GitUnifiedDiffParser.Parse(patch);

        var file = Assert.Single(diff.FileDiffs);
        Assert.Equal("foo.txt", file.Path);
        Assert.Equal("modified", file.Status);
        Assert.Equal(2, file.Additions);
        Assert.Equal(1, file.Deletions);
        Assert.NotNull(file.Patch);
        Assert.Equal(2, diff.Stats.Additions);
        Assert.Equal(1, diff.Stats.Deletions);
        Assert.Equal(1, diff.Stats.FilesChanged);
    }

    [Fact]
    public void Parse_NewFile_MarkedAdded()
    {
        const string patch = """
            diff --git a/new.txt b/new.txt
            new file mode 100644
            index 0000000..a1b2c3d
            --- /dev/null
            +++ b/new.txt
            @@ -0,0 +1,2 @@
            +hello
            +world
            """;

        var file = Assert.Single(GitUnifiedDiffParser.Parse(patch).FileDiffs);
        Assert.Equal("new.txt", file.Path);
        Assert.Equal("added", file.Status);
        Assert.Equal(2, file.Additions);
        Assert.Equal(0, file.Deletions);
    }

    [Fact]
    public void Parse_DeletedFile_PathFromMinusSide()
    {
        const string patch = """
            diff --git a/gone.txt b/gone.txt
            deleted file mode 100644
            index a1b2c3d..0000000
            --- a/gone.txt
            +++ /dev/null
            @@ -1,1 +0,0 @@
            -bye
            """;

        var file = Assert.Single(GitUnifiedDiffParser.Parse(patch).FileDiffs);
        Assert.Equal("gone.txt", file.Path);
        Assert.Equal("deleted", file.Status);
        Assert.Equal(1, file.Deletions);
    }

    [Fact]
    public void Parse_BinaryFile_HasNoPatch()
    {
        const string patch = """
            diff --git a/img.png b/img.png
            new file mode 100644
            index 0000000..a1b2c3d
            Binary files /dev/null and b/img.png differ
            """;

        var file = Assert.Single(GitUnifiedDiffParser.Parse(patch).FileDiffs);
        Assert.Equal("img.png", file.Path);
        Assert.Null(file.Patch);
    }

    [Fact]
    public void Parse_MultipleFiles_SplitsEachBlock()
    {
        const string patch = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1 +1 @@
            -a
            +b
            diff --git a/c.txt b/c.txt
            --- a/c.txt
            +++ b/c.txt
            @@ -0,0 +1 @@
            +c
            """;

        var diff = GitUnifiedDiffParser.Parse(patch);
        Assert.Equal(2, diff.Stats.FilesChanged);
        Assert.Equal(new[] { "a.txt", "c.txt" }, diff.FileDiffs.Select(f => f.Path));
    }
}
