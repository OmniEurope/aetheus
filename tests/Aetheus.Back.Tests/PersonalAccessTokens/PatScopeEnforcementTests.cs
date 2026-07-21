// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.PersonalAccessTokens;

namespace Aetheus.Back.Tests.PersonalAccessTokens;

public class PatScopeEnforcementTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("post")] // case-insensitive
    public void ReadOnlyPat_BlocksMutatingMethods(string method)
        => Assert.True(PatScopeEnforcement.IsWriteBlocked(isReadOnlyPat: true, method));

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("get")]
    public void ReadOnlyPat_AllowsSafeMethods(string method)
        => Assert.False(PatScopeEnforcement.IsWriteBlocked(isReadOnlyPat: true, method));

    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public void ReadWritePat_NeverBlocked(string method)
        => Assert.False(PatScopeEnforcement.IsWriteBlocked(isReadOnlyPat: false, method));

    [Fact]
    public void ReadOnlyPat_AllowsGitUploadPackPost()
        => Assert.False(PatScopeEnforcement.IsWriteBlocked(true, "POST", "/git/1/repo.git/git-upload-pack"));

    [Theory]
    [InlineData("/git/1/repo.git/git-receive-pack")]
    [InlineData("/api/pipelines")]
    public void ReadOnlyPat_BlocksOtherPosts(string path)
        => Assert.True(PatScopeEnforcement.IsWriteBlocked(true, "POST", path));
}
