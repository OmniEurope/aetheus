// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Releases;

public partial class RollbackReleaseDialog
{
    [Parameter] public string SourceVersion { get; set; } = string.Empty;
    [Parameter] public string TargetVersion { get; set; } = string.Empty;
    [Parameter] public string DeploymentAge { get; set; } = string.Empty;
    [Parameter] public int ProjectId { get; set; }
    [Parameter] public List<PipelineDto> Pipelines { get; set; } = [];

    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private OmniDialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private int? _pipelineId;
    private int? _backupRunId;
    private bool _restoreDatabase;
    private bool _canRestoreDatabase;
    private List<BackupOption> _verifiedBackups = [];

    protected override async Task OnInitializedAsync()
    {
        // A backup list the API cannot serve must not fault the dialog: the rollback itself does not
        // depend on it, and the database-restore option simply stays unavailable, exactly as when no
        // verified backup exists.
        try
        {
            var policies = await Api.Security.GetAllBackupPoliciesAsync();
            var projectPolicies = policies.Where(policy => policy.ProjectId == ProjectId).ToList();
            var options = await Task.WhenAll(projectPolicies.Select(LoadVerifiedBackupsAsync));
            _verifiedBackups = options.SelectMany(item => item).ToList();
            _canRestoreDatabase = _verifiedBackups.Count > 0;
        }
        catch (HttpRequestException)
        {
            _verifiedBackups = [];
            _canRestoreDatabase = false;
        }
    }

    private async Task<List<BackupOption>> LoadVerifiedBackupsAsync(BackupPolicyDto policy)
    {
        try
        {
            var runs = await Api.Security.GetAllBackupRunsAsync(policy.Id);
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
