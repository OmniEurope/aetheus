// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Pages.PackageFeeds;
using Bunit;

namespace Aetheus.Front.Tests.Pages.PackageFeeds;

public sealed class PackageFeedEditDialogTests : BunitContext
{
    public PackageFeedEditDialogTests()
        => BunitTestHelper.RegisterServices(this, isAdmin: true);

    [Fact]
    public void CreationDialog_OffersOnlyImplementedFeedTypesAndExplainsTheBoundary()
    {
        var cut = Render<PackageFeedEditDialog>();
        Assert.Equal(3, PackageFeedEditDialog.SupportedCreationTypeCount);
        Assert.Contains("PackageFeedCreationSupportedTypes", cut.Markup, StringComparison.Ordinal);
    }
}
