// SPDX-License-Identifier: EUPL-1.2
using System.Reflection;
using Aetheus.Front.Pages.PackageFeeds;
using Aetheus.Shared.DTOs;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Radzen;

namespace Aetheus.Front.Tests.Pages.PackageFeeds;

public sealed class PackageFeedPackagesDialogTests : BunitContext
{
    private readonly BunitTestHelper.TestHandler _handler;

    public PackageFeedPackagesDialogTests() =>
        _handler = BunitTestHelper.RegisterServices(this, isAdmin: true);

    [Fact]
    public async Task RemoveAsync_RequiresConfirmationNamingPackageAndFeed()
    {
        var package = new PackageEntryDto { Id = 7, PackageFeedId = 3, Name = "Serilog" };
        _handler.SetJsonResponse("api/package-feeds/3", new PackageFeedDetailDto
        {
            Id = 3,
            Name = "corp-nuget",
            Packages = [package]
        });
        _handler.SetResponse(HttpMethod.Delete, "api/package-feeds/3/packages/7",
            System.Net.HttpStatusCode.NoContent);
        var cut = Render<PackageFeedPackagesDialog>(p => p.Add(x => x.FeedId, 3));
        cut.WaitForAssertion(() => Assert.Contains("Serilog", cut.Markup));
        var remove = typeof(PackageFeedPackagesDialog)
            .GetMethod("RemoveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var message = (string)typeof(PackageFeedPackagesDialog)
            .GetMethod("RemoveConfirmationMessage", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, [package])!;
        var dialog = Services.GetRequiredService<DialogService>();
        Assert.Contains("Serilog", message);
        Assert.Contains("corp-nuget", message);

        var cancelled = cut.InvokeAsync(() => (Task)remove.Invoke(cut.Instance, [package])!);
        await cut.InvokeAsync(() => dialog.Close(false));
        await cancelled;
        Assert.DoesNotContain(_handler.Requests, request => request.Method == "DELETE");

        var confirmed = cut.InvokeAsync(() => (Task)remove.Invoke(cut.Instance, [package])!);
        await cut.InvokeAsync(() => dialog.Close(true));
        await confirmed;

        Assert.Contains(_handler.Requests, request => request.Method == "DELETE"
            && request.Url.Contains("api/package-feeds/3/packages/7", StringComparison.Ordinal));
    }
}
