// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;
using Radzen.Blazor;

namespace Aetheus.Front.Pages.Settings;

public partial class PersonalAccessTokens
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private List<PersonalAccessTokenDto> _tokens = [];
    private RadzenDataGrid<PersonalAccessTokenDto>? _grid;
    private int _totalCount;
    private int _page = 1;
    private int _pageSize = 25;
    private string? _sortBy;
    private bool _sortDescending = true;
    private bool _loading = true;
    private bool _creating;

    private readonly NewTokenModel _form = new();

    /// <summary>Plaintext token shown exactly once right after creation, then dismissed forever.</summary>
    private string? _revealedToken;

    private static readonly PatScope[] Scopes = [PatScope.ReadOnly, PatScope.ReadWrite];

    private sealed class NewTokenModel
    {
        [Required]
        [StringLength(64, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        public PatScope Scope { get; set; } = PatScope.ReadOnly;

        [Range(1, 365)]
        public int ExpirationDays { get; set; } = 30;
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        _pageSize = args.Top ?? 25;
        _page = ((args.Skip ?? 0) / _pageSize) + 1;
        (_sortBy, _sortDescending) = ResolveSort(args);
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        _loading = true;
        try
        {
            var result = await Api.GetPersonalAccessTokensAsync(
                _page, _pageSize, sortBy: _sortBy, sortDescending: _sortDescending);
            _tokens = result.Items;
            _totalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _tokens = [];
            _totalCount = 0;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadAsync()
    {
        if (_grid is not null)
            await _grid.Reload();
        else
            await LoadPageAsync();
    }

    private static (string? SortBy, bool Descending) ResolveSort(LoadDataArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return ("CreatedAt", true);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private async Task CreateAsync()
    {
        _creating = true;
        try
        {
            var created = await Api.CreatePersonalAccessTokenAsync(new CreatePersonalAccessTokenRequest
            {
                Name = _form.Name,
                Scope = _form.Scope,
                ExpirationDays = _form.ExpirationDays
            });

            if (created is null)
            {
                Toast.Error("PatCreateFailed", "PatCreateFailed");
                return;
            }

            _revealedToken = created.PlaintextToken;
            _form.Name = string.Empty;
            _form.Scope = PatScope.ReadOnly;
            _form.ExpirationDays = 30;
            Toast.Success("PatCreated", "PatCreated");
            await LoadAsync();
        }
        finally
        {
            _creating = false;
        }
    }

    private Task CopyRevealedAsync()
        => _revealedToken is null ? Task.CompletedTask : Clipboard.CopyAsync(_revealedToken, L["CopiedToClipboard"]);

    private void DismissReveal() => _revealedToken = null;

    private async Task RevokeAsync(PersonalAccessTokenDto token)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["PatRevokeConfirm"].Value, token.Name), L["PatRevoke"].Value,
            new ConfirmOptions { OkButtonText = L["PatRevoke"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.RevokePersonalAccessTokenAsync(token.Id);
        if (status.Success)
        {
            Toast.Success("PatRevoked", "PatRevoked");
            await LoadAsync();
        }
        else
        {
            Toast.Error("PatRevokeFailed", "PatRevokeFailed");
        }
    }

    private string ScopeLabel(PatScope scope) =>
        scope == PatScope.ReadOnly ? L["PatScopeReadOnly"] : L["PatScopeReadWrite"];

    private BadgeStyle StatusStyle(PersonalAccessTokenDto t) =>
        t.RevokedAt is not null ? BadgeStyle.Light
        : t.ExpiresAt <= DateTime.Now ? BadgeStyle.Warning
        : BadgeStyle.Success;

    private string StatusLabel(PersonalAccessTokenDto t) =>
        t.RevokedAt is not null ? L["PatStatusRevoked"]
        : t.ExpiresAt <= DateTime.Now ? L["PatStatusExpired"]
        : L["PatStatusActive"];

    private static bool IsRevocable(PersonalAccessTokenDto t) => t.RevokedAt is null;
}
