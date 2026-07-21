// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Shared;

public partial class RollbackReleaseDialog
{
    [Parameter] public string SourceVersion { get; set; } = string.Empty;
    [Parameter] public string TargetVersion { get; set; } = string.Empty;
    [Parameter] public string DeploymentAge { get; set; } = string.Empty;
    [Parameter] public int ProjectId { get; set; }
    [Parameter] public List<PipelineDto> Pipelines { get; set; } = [];

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private int? _pipelineId;
    private int? _backupRunId;
    private bool _restoreDatabase;
    private bool _canRestoreDatabase;
    private List<BackupOption> _verifiedBackups = [];

    protected override async Task OnInitializedAsync()
    {
        var policies = await Api.GetAllBackupPoliciesAsync();
        var projectPolicies = policies.Where(policy => policy.ProjectId == ProjectId).ToList();
        var options = await Task.WhenAll(projectPolicies.Select(LoadVerifiedBackupsAsync));
        _verifiedBackups = options.SelectMany(item => item).ToList();
        _canRestoreDatabase = _verifiedBackups.Count > 0;
    }

    private async Task<List<BackupOption>> LoadVerifiedBackupsAsync(BackupPolicyDto policy)
    {
        try
        {
            var runs = await Api.GetAllBackupRunsAsync(policy.Id);
            return runs
                .Where(r => r.Status == BackupRunStatus.Succeeded && r.RestoreCheckStatus == RestoreCheckStatus.Verified)
                .Select(r => new BackupOption(r.Id, $"{policy.Name} · {r.StartedAt:g}"))
                .ToList();
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private void OnRestoreDatabaseChanged(bool value)
    {
        _restoreDatabase = value;
        _backupRunId = null;
    }

    private void Confirm() => Dialog.Close(new RollbackReleaseRequest
    {
        PipelineId = _pipelineId!.Value,
        RestoreDatabase = _restoreDatabase,
        BackupRunId = _restoreDatabase ? _backupRunId : null
    });

    private sealed record BackupOption(int Id, string Label);
}
