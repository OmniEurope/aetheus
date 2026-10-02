// SPDX-License-Identifier: EUPL-1.2
using System.Net.Http;
using Aetheus.Front.Components.Servers.ServerDetailSections;
using Bunit;

namespace Aetheus.Front.Tests.Pages.Servers;

public class ServerPatchSectionTests : BunitContext
{
    private static ServerSecurityUpdatesDto Known(int total, int security, bool patchCap) => new()
    {
        Known = true,
        PackageManagerPresent = true,
        PatchManagementAvailable = patchCap,
        PendingTotal = total,
        PendingSecurity = security,
        CheckedAt = DateTime.Now,
        Updates =
        [
            new PendingUpdateDto { Package = "openssl", CurrentVersion = "3.0.2", CandidateVersion = "3.0.3", IsSecurity = true },
            new PendingUpdateDto { Package = "vim", CurrentVersion = "8.2", CandidateVersion = "8.3", IsSecurity = false }
        ]
    };

    private IRenderedComponent<ServerPatchSection> RenderWith(ServerSecurityUpdatesDto dto, bool? patchCap)
    {
        var handler = BunitTestHelper.RegisterServices(this);
        handler.SetJsonResponse(HttpMethod.Get, "security-updates", dto);
        return Render<ServerPatchSection>(p => p
            .Add(c => c.ServerId, 1)
            .Add(c => c.PatchManagementAvailable, patchCap));
    }

    [Fact]
    public void KnownStatus_RendersCountsAndPackages()
    {
        var cut = RenderWith(Known(2, 1, patchCap: true), patchCap: true);

        Assert.Contains("openssl", cut.Markup);
        Assert.Contains("vim", cut.Markup);
        Assert.Contains("PatchSecurityCount", cut.Markup); // security badge label key
        Assert.Contains("PatchPendingCount", cut.Markup);
    }

    [Fact]
    public void WithoutPatchCapability_ShowsApplyDisabledHint()
    {
        var cut = RenderWith(Known(2, 1, patchCap: false), patchCap: false);

        Assert.Contains("PatchApplyDisabledHint", cut.Markup);
    }

    [Fact]
    public void WithPatchCapability_NoDisabledHint()
    {
        var cut = RenderWith(Known(2, 1, patchCap: true), patchCap: true);

        Assert.DoesNotContain("PatchApplyDisabledHint", cut.Markup);
    }

    [Fact]
    public void NotAptServer_ShowsNotApplicable_NotZero()
    {
        var cut = RenderWith(new ServerSecurityUpdatesDto { Known = false, PackageManagerPresent = false }, patchCap: null);

        Assert.Contains("PatchNotApplicableTitle", cut.Markup);
    }

    [Fact]
    public void ProbeNeverSucceeded_ShowsUnknown_NotUpToDate()
    {
        var cut = RenderWith(new ServerSecurityUpdatesDto { Known = false, PackageManagerPresent = true }, patchCap: true);

        Assert.Contains("PatchUnknown", cut.Markup);
    }
}
