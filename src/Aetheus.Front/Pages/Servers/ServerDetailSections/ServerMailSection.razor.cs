// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerMailSection
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private ILogger<ServerMailSection> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }

    private bool _actionRunning;

    // Data
    private List<MailDomainDto> _domains = [];
    private List<MailAccountDto> _accounts = [];
    private List<MailQueueItemDto> _queueItems = [];
    private List<MailDomainDto> _dialogDomains = [];
    private int _domainCount;
    private int _accountCount;
    private int _aliasCount;
    private bool _domainsLoading;
    private bool _accountsLoading;
    private bool _aliasesLoading;
    private int? _loadedServerId;
    private int _loadGeneration;
    private Aetheus.Front.Shared.AetheusDataGrid<MailDomainDto>? _domainsGrid;
    private Aetheus.Front.Shared.AetheusDataGrid<MailAccountDto>? _accountsGrid;
    private Aetheus.Front.Shared.AetheusDataGrid<MailAliasDto>? _aliasesGrid;

    // Add domain dialog
    private string _newDomainName = string.Empty;
    private string _newDkimSelector = "default";

    // Add account dialog
    private string _newAccountEmail = string.Empty;
    private string _newAccountPassword = string.Empty;
    private int _newAccountQuota = 1024;

    // DNS records dialog
    private MailDnsRecordsDto? _dnsRecords;

    // Aliases
    private List<MailAliasDto> _aliases = [];
    private string _newAliasSource = string.Empty;
    private string _newAliasDest = string.Empty;
    private int _selectedAliasDomainId;

    // DKIM rotation dialog
    private string _dkimRotationDomain = string.Empty;
    private string _dkimCurrentSelector = string.Empty;
    private string _dkimNewSelector = string.Empty;
    private int _dkimRotationDomainId;

    // Setup wizard
    private string _setupHostname = string.Empty;
    private string _setupDomain = string.Empty;
    private string _setupDkimSelector = "default";
    private string _setupAdminEmail = string.Empty;
    private string _setupAdminPassword = string.Empty;
    private int _setupQuota = 1024;

    // Logs
    private string _logType = "postfix";
    private int _logLines = 100;
    private readonly List<string> _logTypes = ["postfix", "dovecot"];

    private MailDataDto Mail => Server.Mail;

    private List<string> MailResourceNames => _domains.Select(d => d.Name).ToList();

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        Interlocked.Increment(ref _loadGeneration);
        _domains = [];
        _accounts = [];
        _aliases = [];
        _dialogDomains = [];
        _domainCount = 0;
        _accountCount = 0;
        _aliasCount = 0;
        if (Mail.IsInstalled)
            await ReloadDataAsync();
    }

    private async Task ReloadDataAsync()
    {
        var domainsReload = _domainsGrid?.Reload() ?? Task.CompletedTask;
        var accountsReload = _accountsGrid?.Reload() ?? Task.CompletedTask;
        var aliasesReload = _aliasesGrid?.Reload() ?? Task.CompletedTask;
        await Task.WhenAll(domainsReload, accountsReload, aliasesReload);
    }

    private async Task LoadDomainsAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, descending) = GetSort(args, "Name");
        await LoadPageAsync(
            () => Api.GetMailDomainsPageAsync(ServerId, page, pageSize, sortBy: sortBy, sortDescending: descending),
            result => { _domains = result.Items; _domainCount = result.TotalCount; },
            value => _domainsLoading = value);
    }

    private async Task LoadAccountsAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, descending) = GetSort(args, "Email");
        await LoadPageAsync(
            () => Api.GetMailAccountsPageAsync(ServerId, page, pageSize, sortBy: sortBy, sortDescending: descending),
            result => { _accounts = result.Items; _accountCount = result.TotalCount; },
            value => _accountsLoading = value);
    }

    private async Task LoadAliasesAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, descending) = GetSort(args, "SourceEmail");
        await LoadPageAsync(
            () => Api.GetMailAliasesPageAsync(ServerId, page, pageSize, sortBy: sortBy, sortDescending: descending),
            result => { _aliases = result.Items; _aliasCount = result.TotalCount; },
            value => _aliasesLoading = value);
    }

    private async Task LoadPageAsync<T>(
        Func<Task<PaginatedResult<T>>> load,
        Action<PaginatedResult<T>> apply,
        Action<bool> setLoading)
    {
        var serverId = ServerId;
        var generation = _loadGeneration;
        setLoading(true);
        try
        {
            var result = await load();
            if (serverId == ServerId && generation == _loadGeneration)
                apply(result);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail page load failed for server {ServerId}", serverId);
            if (serverId == ServerId && generation == _loadGeneration)
                Toast.Error(L["LoadFailed"]);
        }
        finally
        {
            if (serverId == ServerId && generation == _loadGeneration)
                setLoading(false);
        }
    }

    private static (string SortBy, bool Descending) GetSort(LoadDataArgs args, string fallback)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return (fallback, false);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ExecuteActionAsync(MailAction action)
    {
        _actionRunning = true;
        try
        {
            var success = await Api.ExecuteMailActionAsync(ServerId, new MailActionRequest { Action = action });
            if (success)
                Toast.Success(L["TaskQueued"]);
            else
                Toast.Error(L["ActionFailed"]);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail action {Action} failed for server {ServerId}", action, ServerId);
            Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private async Task CreateDomainAsync()
    {
        try
        {
            var result = await Api.CreateMailDomainAsync(ServerId, new CreateMailDomainRequest
            {
                Name = _newDomainName,
                DkimSelector = _newDkimSelector
            });
            if (result is not null)
            {
                Toast.Success(L["TaskQueued"]);
                _newDomainName = string.Empty;
                _newDkimSelector = "default";
                await ReloadDataAsync();
            }
            else
            {
                Toast.Error(L["ActionFailed"]);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail domain creation failed for server {ServerId}", ServerId);
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task OpenAddDomainDialogAsync()
    {
        var result = await OpenMailDialogAsync(MailDialogMode.AddDomain, L["AddDomain"], new MailDialogModel { Domain = _newDomainName, DkimSelector = _newDkimSelector });
        if (result is null) return;
        _newDomainName = result.Domain;
        _newDkimSelector = result.DkimSelector;
        await CreateDomainAsync();
    }

    private async Task DeleteDomainAsync(int domainId)
    {
        var success = await Api.DeleteMailDomainAsync(ServerId, domainId);
        if (success)
        {
            Toast.Success(L["TaskQueued"]);
            await ReloadDataAsync();
        }
        else
        {
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task CreateAccountAsync()
    {
        try
        {
            var result = await Api.CreateMailAccountAsync(ServerId, new CreateMailAccountRequest
            {
                Email = _newAccountEmail,
                Password = _newAccountPassword,
                QuotaMb = _newAccountQuota
            });
            if (result is not null)
            {
                Toast.Success(L["TaskQueued"]);
                _newAccountEmail = string.Empty;
                _newAccountPassword = string.Empty;
                _newAccountQuota = 1024;
                await ReloadDataAsync();
            }
            else
            {
                Toast.Error(L["ActionFailed"]);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail account creation failed for server {ServerId}", ServerId);
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task OpenAddAccountDialogAsync()
    {
        var result = await OpenMailDialogAsync(MailDialogMode.AddAccount, L["AddAccount"], new MailDialogModel { Email = _newAccountEmail, Password = _newAccountPassword, QuotaMb = _newAccountQuota });
        if (result is null) return;
        _newAccountEmail = result.Email;
        _newAccountPassword = result.Password;
        _newAccountQuota = result.QuotaMb;
        await CreateAccountAsync();
    }

    private async Task DeleteAccountAsync(int accountId)
    {
        var success = await Api.DeleteMailAccountAsync(ServerId, accountId);
        if (success)
        {
            Toast.Success(L["TaskQueued"]);
            await ReloadDataAsync();
        }
        else
        {
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task ShowDnsRecordsAsync(MailDomainDto domain)
    {
        try
        {
            _dnsRecords = await Api.GetMailDnsRecordsAsync(ServerId, domain.Id);
            await Dialog.OpenAsync<MailOperationDialog>(
                $"{L["DnsRecords"]}: {_dnsRecords?.Domain}",
                new Dictionary<string, object?> { { "Mode", MailDialogMode.DnsRecords }, { "DnsRecords", _dnsRecords } },
                new DialogOptions { Width = "42rem" });
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail DNS lookup failed for server {ServerId}, domain {DomainId}", ServerId, domain.Id);
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task FetchLogsAsync()
    {
        var success = await Api.GetMailLogsAsync(ServerId, new MailLogRequest
        {
            LogType = _logType,
            Lines = _logLines
        });
        if (success)
            Toast.Success(L["TaskQueued"]);
        else
            Toast.Error(L["ActionFailed"]);
    }

    private async Task RunSetupAsync()
    {
        try
        {
            var success = await Api.SetupMailAsync(ServerId, new MailSetupRequest
            {
                Hostname = _setupHostname,
                Domain = _setupDomain,
                DkimSelector = _setupDkimSelector,
                AdminEmail = _setupAdminEmail,
                AdminPassword = _setupAdminPassword,
                QuotaMb = _setupQuota
            });
            if (success)
            {
                Toast.Success(L["TaskQueued"]);
            }
            else
            {
                Toast.Error(L["ActionFailed"]);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Mail setup failed for server {ServerId}", ServerId);
            Toast.Error(L["ActionFailed"]);
        }
    }

    private async Task OpenSetupDialogAsync()
    {
        var result = await OpenMailDialogAsync(MailDialogMode.Setup, L["MailSetup"], new MailDialogModel
        {
            Hostname = _setupHostname,
            Domain = _setupDomain,
            DkimSelector = _setupDkimSelector,
            Email = _setupAdminEmail,
            Password = _setupAdminPassword,
            QuotaMb = _setupQuota
        }, "38rem");
        if (result is null) return;
        _setupHostname = result.Hostname;
        _setupDomain = result.Domain;
        _setupDkimSelector = result.DkimSelector;
        _setupAdminEmail = result.Email;
        _setupAdminPassword = result.Password;
        _setupQuota = result.QuotaMb;
        await RunSetupAsync();
    }

    private async Task<MailDialogModel?> OpenMailDialogAsync(MailDialogMode mode, string title, MailDialogModel model, string width = "32rem")
    {
        if (mode is MailDialogMode.AddAccount or MailDialogMode.AddAlias && _dialogDomains.Count == 0)
            _dialogDomains = await Api.GetMailDomainsAsync(ServerId);
        var result = await Dialog.OpenAsync<MailOperationDialog>(title,
            new Dictionary<string, object?>
            {
                { "Mode", mode },
                { "Model", model },
                { "Domains", _dialogDomains.Count > 0 ? _dialogDomains : _domains }
            },
            new DialogOptions { Width = width });
        return result as MailDialogModel;
    }

    public void HandleTaskCompleted()
    {
        _ = InvokeAsync(async () =>
        {
            if (Mail.IsInstalled)
                await ReloadDataAsync();
            StateHasChanged();
        });
    }

    private async Task ShowConfirm(string title, string message, Func<Task> action)
    {
        var confirmed = await Dialog.Confirm(message, title,
            new ConfirmOptions { OkButtonText = L["Confirm"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed == true)
            await action();
    }

    private async Task CopyToClipboardAsync(string text)
    {
        await Js.InvokeVoidAsync("navigator.clipboard.writeText", text);
        Toast.Success(L["CopiedToClipboard"]);
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    // --- Aliases ---

    private async Task CreateAliasAsync()
    {
        var result = await Api.CreateMailAliasAsync(ServerId, _selectedAliasDomainId, new CreateMailAliasRequest
        {
            SourceEmail = _newAliasSource,
            DestinationEmail = _newAliasDest
        });
        if (result is not null)
        {
            _newAliasSource = string.Empty;
            _newAliasDest = string.Empty;
            Toast.Success(L["AliasCreated"]);
            if (_aliasesGrid is not null) await _aliasesGrid.Reload();
        }
        else
            Toast.Error(L["OperationFailed"]);
    }

    private async Task OpenAddAliasDialogAsync()
    {
        var result = await OpenMailDialogAsync(MailDialogMode.AddAlias, L["AddAlias"], new MailDialogModel
        {
            Source = _newAliasSource,
            Destination = _newAliasDest,
            DomainId = _selectedAliasDomainId > 0
                ? _selectedAliasDomainId
                : _dialogDomains.FirstOrDefault()?.Id ?? _domains.FirstOrDefault()?.Id ?? 0
        });
        if (result is null) return;
        _newAliasSource = result.Source;
        _newAliasDest = result.Destination;
        _selectedAliasDomainId = result.DomainId;
        await CreateAliasAsync();
    }

    private async Task DeleteAliasAsync(int aliasId)
    {
        if (await Api.DeleteMailAliasAsync(ServerId, aliasId))
        {
            Toast.Success(L["AliasDeleted"]);
            if (_aliasesGrid is not null) await _aliasesGrid.Reload();
        }
        else
            Toast.Error(L["OperationFailed"]);
    }

    // --- DKIM key rotation ---

    private async Task ShowDkimRotation(MailDomainDto domain)
    {
        _dkimRotationDomainId = domain.Id;
        _dkimRotationDomain = domain.Name;
        _dkimCurrentSelector = domain.DkimSelector;
        _dkimNewSelector = string.Empty;
        var result = await OpenMailDialogAsync(MailDialogMode.RotateDkim, L["RotateDkimKey"], new MailDialogModel
        {
            Domain = _dkimRotationDomain,
            CurrentSelector = _dkimCurrentSelector,
            NewSelector = _dkimNewSelector
        });
        if (result is null) return;
        _dkimNewSelector = result.NewSelector;
        await RotateDkimKeyAsync();
    }

    private async Task RotateDkimKeyAsync()
    {
        var result = await Api.RotateMailDkimKeyAsync(ServerId, _dkimRotationDomainId, new DkimRotationRequest
        {
            NewSelector = _dkimNewSelector
        });
        if (result is not null)
        {
            Toast.Success(L["DkimKeyRotated"]);
            await ReloadDataAsync();
        }
        else
            Toast.Error(L["OperationFailed"]);
    }

}
