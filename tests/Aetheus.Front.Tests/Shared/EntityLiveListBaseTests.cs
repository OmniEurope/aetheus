// SPDX-License-Identifier: EUPL-1.2
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aetheus.Front.Tests.Shared;

/// <summary>
/// Audit of 2026-09-30: the shared live list of <c>PipelineFleet</c> and <c>VaultsList</c>. A push
/// about a followed type refreshes the list, another type does not; the permission subscription ends
/// with the list; and a hub that cannot be reached leaves the list working.
/// </summary>
public sealed class EntityLiveListBaseTests : BunitContext
{
    public EntityLiveListBaseTests() => BunitTestHelper.RegisterServices(this);

    /// <summary>A list that follows vaults and counts what reaches it.</summary>
    private sealed class VaultProbe : Aetheus.Front.Components.Shared.EntityLiveListBase
    {
        public int Refreshes { get; private set; }

        public int PermissionChanges { get; private set; }

        protected override async Task OnInitializedAsync()
        {
            FollowPermissions();
            await FollowEntitiesAsync(ResourceType.Vault);
        }

        protected override Task OnEntitiesChangedAsync()
        {
            Refreshes++;
            return Task.CompletedTask;
        }

        protected override void OnPermissionsChanged() => PermissionChanges++;
    }

    [Fact]
    public async Task APushAboutAFollowedType_RefreshesTheList_AnotherTypeDoesNot()
    {
        // The test hub cannot be reached: the list still renders, and its handler still decides.
        var cut = Render<VaultProbe>();

        await cut.InvokeAsync(() => cut.Instance.OnEntityChangedAsync(ResourceType.Pipeline));
        Assert.Equal(0, cut.Instance.Refreshes);

        await cut.InvokeAsync(() => cut.Instance.OnEntityChangedAsync(ResourceType.Vault));
        Assert.Equal(1, cut.Instance.Refreshes);
    }

    [Fact]
    public async Task ThePermissionSubscription_EndsWithTheList()
    {
        var cut = Render<VaultProbe>();
        var permissions = Services.GetRequiredService<PermissionService>();

        permissions.SetPermissions([], isAdmin: false);
        Assert.Equal(1, cut.Instance.PermissionChanges);

        await cut.Instance.DisposeAsync();
        permissions.SetPermissions([], isAdmin: false);
        Assert.Equal(1, cut.Instance.PermissionChanges);
    }
}
