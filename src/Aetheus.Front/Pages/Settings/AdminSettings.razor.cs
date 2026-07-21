// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.Constants;
using Aetheus.Shared.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Settings;

public partial class AdminSettings : IAsyncDisposable
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private AuthStateProvider Auth { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private HubConnectionFactory HubFactory { get; set; } = default!;

    private AdminEntitySubscription? _adminRt;

    private List<AppSettingDto> _settings = [];
    private List<SecretDto> _secrets = [];
    private List<RegistrationTokenDto> _tokens = [];
    private Dictionary<string, string> _editBuffer = [];
    private readonly HashSet<string> _savingKeys = [];
    private readonly HashSet<int> _revealedTokens = [];
    private string _newSecretKey = string.Empty;
    private string _newSecretValue = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.IsAdmin)
        {
            Nav.NavigateTo("/");
            return;
        }

        Breadcrumb.Set(
            new BreadcrumbItem(L["Administration"], "/admin"),
            new BreadcrumbItem(L["PlatformSettings"]));

        try
        {
            var settingsTask = Api.GetSettingsAsync();
            var secretsTask = Api.GetSecretsAsync();
            var tokensTask = Api.GetRegistrationTokensAsync();
            await Task.WhenAll(settingsTask, secretsTask, tokensTask);

            _settings = await settingsTask;
            _secrets = await secretsTask;
            _tokens = await tokensTask;
            _editBuffer = _settings.ToDictionary(s => s.Key, s => s.Value);
        }
        catch (HttpRequestException)
        {
            _settings = [];
            _secrets = [];
            _tokens = [];
        }

        await StartHubAsync();
    }

    // S-FEAT-RT2W / S-TECH-RT5K: refresh the registration-token list in realtime when a token is created
    // elsewhere or consumed by an enrolling agent. Uses the shared AdminEntitySubscription wrapper (the
    // 5th admin block to adopt it, after Users/Roles/Organizations/Plugins) instead of a bespoke hub.
    private Task StartHubAsync()
    {
        _adminRt = new AdminEntitySubscription(HubFactory);
        return _adminRt.StartAsync(AdminEntities.RegistrationToken, () => InvokeAsync(async () =>
        {
            try { _tokens = await Api.GetRegistrationTokensAsync(); } catch (HttpRequestException) { }
            StateHasChanged();
        }));
    }

    private async Task SaveSetting(string key)
    {
        if (_savingKeys.Contains(key)) return;
        if (!_editBuffer.TryGetValue(key, out var value)) return;

        _savingKeys.Add(key);
        try
        {
            var success = await Api.UpdateSettingAsync(key, value);
            if (success)
                Toast.Success("Saved", "SettingSaved", key);
            else
                Toast.Error("Error", "SaveFailed");
        }
        finally
        {
            _savingKeys.Remove(key);
        }
    }

    private async Task AddSecret()
    {
        if (string.IsNullOrWhiteSpace(_newSecretKey) || string.IsNullOrWhiteSpace(_newSecretValue))
        {
            Toast.Warning("ValidationError", "SecretFieldsRequired");
            return;
        }

        var created = await Api.CreateSecretAsync(new CreateSecretRequest { Key = _newSecretKey, Value = _newSecretValue });
        if (created is not null)
        {
            _secrets = await Api.GetSecretsAsync();
            Toast.Success("Added", "SecretAdded");
            _newSecretKey = string.Empty;
            _newSecretValue = string.Empty;
        }
        else
        {
            Toast.Error("Error", "SaveFailed");
        }
    }

    private async Task DeleteSecret(int id)
    {
        var confirmed = await Dialog.Confirm(L["DeleteSecretConfirm"].Value, L["DeleteSecret"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var success = await Api.DeleteSecretAsync(id);
        if (success)
        {
            _secrets = await Api.GetSecretsAsync();
            Toast.Success("Deleted", "SecretDeleted");
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
    }

    private async Task GenerateToken()
    {
        var token = await Api.CreateRegistrationTokenAsync();
        if (token is not null)
        {
            _tokens = await Api.GetRegistrationTokensAsync();
            Toast.Success("Generated", "TokenGenerated");
        }
        else
        {
            Toast.Error("Error", "SaveFailed");
        }
    }

    private void ToggleTokenReveal(int tokenId)
    {
        if (!_revealedTokens.Remove(tokenId))
            _revealedTokens.Add(tokenId);
    }

    // CL8R: route token copy through the shared ClipboardService (one success toast + graceful
    // clipboard-unavailable handling) rather than a bespoke writeText + toast pair.
    private Task CopyToken(string token) => Clipboard.CopyAsync(token, L["TokenCopied"]);

    public async ValueTask DisposeAsync()
    {
        if (_adminRt is not null)
            await _adminRt.DisposeAsync();
    }
}
