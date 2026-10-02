// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Back.Tests.Git;

public class GitBranchNameValidatorTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("develop")]
    [InlineData("release/2026.08")]
    [InlineData("feature/PROJ-12_thing")]
    [InlineData("v1.2.3")]
    public void Accepts_Ordinary_Branch_Names(string branch) =>
        Assert.True(GitBranchNameValidator.IsValid(branch));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // A leading '-' is read by git as an option, not a ref: the argument-injection case.
    [InlineData("--upload-pack=evil")]
    [InlineData("-main")]
    [InlineData(".hidden")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("trailing.")]
    [InlineData("main.lock")]
    [InlineData("a..b")]
    [InlineData("a//b")]
    [InlineData("main@{1}")]
    [InlineData("@")]
    [InlineData("with space")]
    [InlineData("with~tilde")]
    [InlineData("with^caret")]
    [InlineData("with:colon")]
    [InlineData("with?question")]
    [InlineData("with*star")]
    [InlineData("with[bracket")]
    [InlineData("with\\backslash")]
    [InlineData("with\ttab")]
    [InlineData("accentué")]
    public void Rejects_Names_Git_Or_Aetheus_Will_Not_Take(string? branch) =>
        Assert.False(GitBranchNameValidator.IsValid(branch));

    [Fact]
    public void Rejects_A_Name_Past_The_Length_Ceiling()
    {
        Assert.True(GitBranchNameValidator.IsValid(new string('a', GitBranchNameValidator.MaxLength)));
        Assert.False(GitBranchNameValidator.IsValid(new string('a', GitBranchNameValidator.MaxLength + 1)));
    }
}
