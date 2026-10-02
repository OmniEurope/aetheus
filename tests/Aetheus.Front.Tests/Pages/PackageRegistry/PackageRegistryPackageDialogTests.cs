// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Components.PackageRegistry;
using Bunit;

namespace Aetheus.Front.Tests.Pages.PackageRegistry;

public sealed class PackageRegistryPackageDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PackageRegistryPackageDialogTests() =>
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);

    [Fact]
    public void RendersVersionListingState()
    {
        _handler.SetJsonResponse("api/package-registry/7", new PackageRegistryPackageDetailDto
        {
            Id = 7,
            Kind = PackageRegistryKind.Npm,
            Name = "@aetheus/browser",
            Versions = [
                new PackageRegistryVersionDto
                {
                    Id = 9,
                    Version = "2.0.0",
                    IsListed = true,
                    SizeBytes = 2048,
                    CreatedAt = DateTime.UtcNow
                }
            ]
        });

        var cut = Render<PackageRegistryPackageDialog>(parameters =>
            parameters.Add(component => component.PackageId, 7));

        cut.WaitForAssertion(() => Assert.Contains("2.0.0", cut.Markup));
        Assert.Contains("Unlist", cut.Markup);
        Assert.Contains("2 KiB", cut.Markup);
    }
}
