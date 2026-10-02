// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Back.Components.Apache;

namespace Aetheus.Back.Tests.Apache;

/// <summary>
/// F-005. <c>IsValidDocumentRoot</c> is the only thing standing between a caller-supplied path and
/// the .htaccess read/write handlers, and its regex allowed both '.' and '/' in the same character
/// class - so "/var/www/../../etc" satisfied a check whose name promises an absolute document root.
///
/// No privilege escalation was demonstrated (the endpoints already require Server.Write and the
/// written file name is fixed), so this is depth, not a breach. It is still worth a test: the point
/// of the function is that its name is true.
/// </summary>
public sealed class ApacheDocumentRootValidationTests
{
    [Theory]
    [InlineData("/var/www/html")]
    [InlineData("/srv/app-1/public")]
    [InlineData("/var/www/site.example.com")]
    [InlineData("/opt/my_app/web")]
    public void AbsolutePathWithoutTraversal_IsAccepted(string path) =>
        Assert.True(ApacheCommandHelper.IsValidDocumentRoot(path));

    [Theory]
    // The exact string the audit used: every character is in the allowed class, yet it escapes.
    [InlineData("/var/www/../../etc")]
    [InlineData("/..")]
    [InlineData("/var/../etc/passwd")]
    [InlineData("/var/www/..")]
    public void Traversal_IsRejected(string path) =>
        Assert.False(ApacheCommandHelper.IsValidDocumentRoot(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    [InlineData("/var/www; rm -rf /")]
    [InlineData("/var/www/$(whoami)")]
    public void NonAbsoluteOrUnsafePath_IsRejected(string path) =>
        Assert.False(ApacheCommandHelper.IsValidDocumentRoot(path));

    /// <summary>
    /// A dot inside a segment is legitimate and must stay accepted: only a segment that IS ".."
    /// traverses. Without this the fix would break every domain-named document root.
    /// </summary>
    [Fact]
    public void DotInsideASegment_IsNotTreatedAsTraversal() =>
        Assert.True(ApacheCommandHelper.IsValidDocumentRoot("/var/www/..hidden/public"));
}
