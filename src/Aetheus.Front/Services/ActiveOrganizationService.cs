// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Shared.DTOs.Organizations;
using Microsoft.JSInterop;

namespace Aetheus.Front.Services;

/// <summary>
/// Tracks the user's available organizations (loaded from <c>/api/organizations/me</c>) and
/// the currently active organization. The active org id is persisted in localStorage so it
/// survives reloads. UI components subscribe to <see cref="Changed"/> to react to switches.
/// </summary>
public sealed class ActiveOrganizationService(ApiClient api, IJSRuntime js)
{
    private const string StorageKey = "aetheus.activeOrgId";

    public IReadOnlyList<MyOrganizationDto> Available { get; private set; } = [];
    public MyOrganizationDto? Active { get; private set; }
    public bool IsLoaded { get; private set; }

    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var orgs = await api.GetMyOrganizationsAsync(ct).ConfigureAwait(false) ?? [];
        Available = orgs;

        var stored = await TryGetStoredAsync().ConfigureAwait(false);
        Active = orgs.FirstOrDefault(o => o.Id == stored) ?? orgs.FirstOrDefault();
        IsLoaded = true;
        Changed?.Invoke();
    }

    public async Task SetActiveAsync(int organizationId)
    {
        var match = Available.FirstOrDefault(o => o.Id == organizationId);
        if (match is null || (Active is not null && Active.Id == match.Id)) return;
        Active = match;
        await js.InvokeVoidAsync("Aetheus.setLocal", StorageKey, match.Id.ToString()).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Available = [];
        Active = null;
        IsLoaded = false;
        Changed?.Invoke();
    }

    private async Task<int?> TryGetStoredAsync()
    {
        var raw = await js.InvokeAsync<string?>("Aetheus.getLocal", StorageKey).ConfigureAwait(false);
        return int.TryParse(raw, out var id) ? id : null;
    }
}
