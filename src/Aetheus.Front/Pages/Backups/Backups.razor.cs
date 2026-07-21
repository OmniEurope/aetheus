// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Front.Shared;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Backups;

public partial class Backups
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private List<BackupPolicyDto> _policies = [];
    private AetheusDataGrid<BackupPolicyDto>? _policyGrid;
    private int _policiesTotalCount;
    private string? _policySearch;
    private readonly List<DropItem> _projects = [];
    private readonly List<DropItem> _servers = [];
    private bool _loading = true;
    private bool _saving;

    private PolicyForm? _form;                     // non-null while the create/edit panel is open
    private BackupPolicyDto? _runsFor;             // policy whose run history is shown
    private List<BackupRunDto> _runs = [];
    private AetheusDataGrid<BackupRunDto>? _runsGrid;
    private int _runsTotalCount;
    private string? _runsSearch;
    private bool _runsLoading;

    private static readonly BackupDbEngine[] Engines = [BackupDbEngine.None, BackupDbEngine.Postgres, BackupDbEngine.MySql];
    private List<EnumOption<BackupDbEngine>> _engineOptions = [];

    private readonly record struct DropItem(int Id, string Name);
    private readonly record struct EnumOption<T>(string Text, T Value);

    private sealed class PolicyForm
    {
        public int Id { get; set; } // 0 = new

        [Required]
        [StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        [Range(1, int.MaxValue, ErrorMessage = "Select a project")]
        public int ProjectId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Select a server")]
        public int ServerId { get; set; }

        public BackupDbEngine DbEngine { get; set; } = BackupDbEngine.Postgres;
        [StringLength(255)] public string? DbHost { get; set; }
        [Range(1, 65535)] public int? DbPort { get; set; }
        [StringLength(120)] public string? DbName { get; set; }
        [StringLength(120)] public string? DbUser { get; set; }
        [StringLength(256)] public string? DbPassword { get; set; }
        public string FilePathsText { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string ScheduleCron { get; set; } = "0 3 * * *";

        [Range(1, 365)]
        public int RetentionCount { get; set; } = 7;

        [StringLength(120)]
        public string? RestoreCheckCron { get; set; }
    }

    protected override async Task OnInitializedAsync()
    {
        _engineOptions = Engines.Select(value => new EnumOption<BackupDbEngine>(L.Localize(value), value)).ToList();
        Breadcrumb.Set(new BreadcrumbItem(L["Backups"]));
        try
        {
            foreach (var p in await Api.GetAllProjectsAsync())
                _projects.Add(new DropItem(p.Id, p.Name));
            foreach (var s in await Api.GetAllServersAsync())
                _servers.Add(new DropItem(s.Id, s.Name));
        }
        catch (HttpRequestException)
        {
            // Expired-JWT 401 during load must not trip the ErrorBoundary; grids just show empty.
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnLoadPoliciesAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest();
        var (sortBy, sortDescending) = GetSort(args, "Name");
        _loading = true;
        try
        {
            var result = await Api.GetBackupPoliciesAsync(
                page, pageSize, _policySearch, sortBy, sortDescending);
            _policies = result.Items;
            _policiesTotalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            _policies = [];
            _policiesTotalCount = 0;
        }
        finally { _loading = false; }
    }

    private async Task ResetPoliciesAsync()
    {
        if (_policyGrid is not null) await _policyGrid.GoToPage(0);
    }

    private async Task ReloadAsync()
    {
        if (_policyGrid is not null) await _policyGrid.Reload();
    }

    private void NewPolicy() => _form = new PolicyForm();

    private void EditPolicy(BackupPolicyDto p) => _form = new PolicyForm
    {
        Id = p.Id,
        Name = p.Name,
        Enabled = p.Enabled,
        ProjectId = p.ProjectId,
        ServerId = p.ServerId,
        DbEngine = p.DbEngine,
        DbHost = p.DbHost,
        DbPort = p.DbPort,
        DbName = p.DbName,
        DbUser = p.DbUser,
        DbPassword = null, // null = keep existing password
        FilePathsText = string.Join('\n', p.FilePaths),
        ScheduleCron = p.ScheduleCron,
        RetentionCount = p.RetentionCount,
        RestoreCheckCron = p.RestoreCheckCron
    };

    private void CancelForm() => _form = null;

    private async Task SaveAsync()
    {
        if (_form is null) return;
        _saving = true;
        try
        {
            var files = _form.FilePathsText
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (_form.Id == 0)
            {
                var created = await Api.CreateBackupPolicyAsync(new CreateBackupPolicyRequest
                {
                    Name = _form.Name,
                    ProjectId = _form.ProjectId,
                    ServerId = _form.ServerId,
                    DbEngine = _form.DbEngine,
                    DbHost = _form.DbHost,
                    DbPort = _form.DbPort,
                    DbName = _form.DbName,
                    DbUser = _form.DbUser,
                    DbPassword = _form.DbPassword,
                    FilePaths = files,
                    ScheduleCron = _form.ScheduleCron,
                    RetentionCount = _form.RetentionCount,
                    RestoreCheckCron = _form.RestoreCheckCron
                });
                if (created is null) { Toast.Error("BackupSaveFailed", "BackupSaveFailed"); return; }
            }
            else
            {
                var updated = await Api.UpdateBackupPolicyAsync(_form.Id, new UpdateBackupPolicyRequest
                {
                    Name = _form.Name,
                    Enabled = _form.Enabled,
                    DbEngine = _form.DbEngine,
                    DbHost = _form.DbHost,
                    DbPort = _form.DbPort,
                    DbName = _form.DbName,
                    DbUser = _form.DbUser,
                    DbPassword = _form.DbPassword,
                    FilePaths = files,
                    ScheduleCron = _form.ScheduleCron,
                    RetentionCount = _form.RetentionCount,
                    RestoreCheckCron = _form.RestoreCheckCron
                });
                if (updated is null) { Toast.Error("BackupSaveFailed", "BackupSaveFailed"); return; }
            }

            Toast.Success("BackupSaved", "BackupSaved");
            _form = null;
            await ReloadAsync();
        }
        catch (HttpRequestException)
        {
            Toast.Error("BackupSaveFailed", "BackupSaveFailed");
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task DeletePolicyAsync(BackupPolicyDto p)
    {
        var confirmed = await Dialog.Confirm(
            string.Format(L["BackupDeleteConfirm"].Value, p.Name), L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.DeleteBackupPolicyAsync(p.Id);
        if (status.Success)
        {
            Toast.Success("BackupDeleted", "BackupDeleted");
            if (_runsFor?.Id == p.Id) _runsFor = null;
            await ReloadAsync();
        }
        else
        {
            Toast.Error("BackupDeleteFailed", "BackupDeleteFailed");
        }
    }

    private async Task RunNowAsync(BackupPolicyDto p)
    {
        var status = await Api.RunBackupNowAsync(p.Id);
        if (status.Success) Toast.Success("BackupRunQueued", "BackupRunQueued");
        else Toast.Error("BackupRunFailed", "BackupRunFailed");
    }

    private async Task ShowRunsAsync(BackupPolicyDto p)
    {
        _runsFor = p;
        _runs = [];
        _runsTotalCount = 0;
        _runsSearch = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnLoadRunsAsync(LoadDataArgs args)
    {
        if (_runsFor is null) return;
        var policyId = _runsFor.Id;
        var (page, pageSize) = args.ToPageRequest(10);
        var (sortBy, sortDescending) = GetSort(args, "StartedAt", true);
        _runsLoading = true;
        try
        {
            var result = await Api.GetBackupRunsAsync(
                policyId, page, pageSize, _runsSearch, sortBy, sortDescending);
            if (_runsFor?.Id != policyId) return;
            _runs = result.Items;
            _runsTotalCount = result.TotalCount;
        }
        catch (HttpRequestException)
        {
            if (_runsFor?.Id != policyId) return;
            _runs = [];
            _runsTotalCount = 0;
        }
        finally { _runsLoading = false; }
    }

    private async Task ResetRunsAsync()
    {
        if (_runsGrid is not null) await _runsGrid.GoToPage(0);
    }

    private static (string SortBy, bool Descending) GetSort(
        LoadDataArgs args, string defaultSort, bool defaultDescending = false)
    {
        if (string.IsNullOrWhiteSpace(args.OrderBy)) return (defaultSort, defaultDescending);
        var parts = args.OrderBy.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1
            ? string.Equals(parts[1], "desc", StringComparison.OrdinalIgnoreCase)
            : defaultDescending);
    }

    private string ProjectName(int id) => _projects.FirstOrDefault(x => x.Id == id).Name ?? id.ToString();
    private string ServerName(int id) => _servers.FirstOrDefault(x => x.Id == id).Name ?? id.ToString();

    private BadgeStyle RunStatusStyle(BackupRunStatus s) => s switch
    {
        BackupRunStatus.Succeeded => BadgeStyle.Success,
        BackupRunStatus.Failed => BadgeStyle.Danger,
        _ => BadgeStyle.Info
    };

    private BadgeStyle RestoreStyle(RestoreCheckStatus s) => s switch
    {
        RestoreCheckStatus.Verified => BadgeStyle.Success,
        RestoreCheckStatus.Failed => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private string RestoreLabel(RestoreCheckStatus s) => s switch
    {
        RestoreCheckStatus.Verified => L["BackupRestoreVerified"],
        RestoreCheckStatus.Failed => L["BackupRestoreFailed"],
        _ => L["BackupRestoreUnverified"]
    };
}
