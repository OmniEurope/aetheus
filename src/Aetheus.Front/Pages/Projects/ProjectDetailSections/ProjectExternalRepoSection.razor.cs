// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectExternalRepoSection : ProjectWritableSectionBase
{
    [Inject] private UiActions Ui { get; set; } = default!;

    private bool _enabled;
    private bool _busy;
    private bool _syncing;
    private ExternalRepoDto? _repo;
    private AttachModel _model = new();

    private static readonly List<object> _providers = Enum.GetValues<GitProviderType>()
        .Select(t => (object)new { Text = t.ToString(), Value = t }).ToList();
    private static readonly List<object> _authTypes = Enum.GetValues<GitAuthType>()
        .Select(t => (object)new { Text = t.ToString(), Value = t }).ToList();

    protected override async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _enabled = await Api.Git.IsExternalReposEnabledAsync();
            _repo = _enabled ? await Api.Git.GetExternalRepoAsync(ProjectId) : null;
        }
        catch (HttpRequestException) { _repo = null; }
        finally { _loading = false; }
    }

    private async Task AttachAsync()
    {
        _busy = true;
        try
        {
            await Ui.RunAsync(
                () => Api.Git.AttachExternalRepoAsync(new AttachExternalRepoRequest
                {
                    ProjectId = ProjectId,
                    ProviderType = _model.ProviderType,
                    BaseUrl = _model.BaseUrl,
                    OwnerOrGroup = _model.OwnerOrGroup,
                    RepositoryName = _model.RepositoryName,
                    DefaultBranch = _model.DefaultBranch,
                    AuthType = _model.AuthType,
                    Username = _model.Username,
                    Token = _model.Token,
                    PrivateKeyPem = _model.PrivateKeyPem,
                    Passphrase = _model.Passphrase,
                    KnownHosts = _model.KnownHosts,
                    AutoSyncEnabled = _model.AutoSyncEnabled,
                    FetchIntervalMinutes = _model.FetchIntervalMinutes,
                    WriteEnabled = _model.WriteEnabled
                }),
                "ExternalRepoAttached",
                async _ => await LoadAsync(),
                successTitleKey: "Saved");
        }
        finally { _busy = false; }
    }

    private async Task SyncNowAsync()
    {
        _syncing = true;
        StateHasChanged();
        try
        {
            var result = await Api.Git.SyncExternalRepoNowAsync(ProjectId);
            if (result is not null) { _repo = result; Toast.Success("Saved", "ExternalRepoSyncQueued"); }
            else Toast.Error("Error", "SaveFailed");
        }
        catch (HttpRequestException) { Toast.Error("Error", "SaveFailed"); }
        finally { _syncing = false; StateHasChanged(); }
    }

    private async Task DetachAsync()
    {
        var confirmed = await Dialog.Confirm(L["DetachExternalRepoConfirm"].Value, L["Detach"].Value,
            new ConfirmOptions { OkButtonText = L["Detach"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var status = await Api.Git.DetachExternalRepoAsync(ProjectId);
        if (status.Success) { _repo = null; StateHasChanged(); }
        else Toast.Error("Error", "DeleteFailed");
    }

    private static BadgeStyle MirrorBadge(GitMirrorStatus status) => status switch
    {
        GitMirrorStatus.Ready => BadgeStyle.Success,
        GitMirrorStatus.Syncing => BadgeStyle.Info,
        GitMirrorStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Light
    };

    private sealed class AttachModel
    {
        public GitProviderType ProviderType { get; set; } = GitProviderType.GitHub;

        [StringLength(500)]
        public string? BaseUrl { get; set; }

        [Required]
        [StringLength(200)]
        public string OwnerOrGroup { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string RepositoryName { get; set; } = string.Empty;

        [StringLength(200)]
        public string? DefaultBranch { get; set; }

        public GitAuthType AuthType { get; set; } = GitAuthType.HttpsToken;

        [StringLength(200)]
        public string? Username { get; set; }

        [StringLength(4000)]
        public string? Token { get; set; }

        [StringLength(20000)]
        public string? PrivateKeyPem { get; set; }

        [StringLength(500)]
        public string? Passphrase { get; set; }

        [StringLength(8000)]
        public string? KnownHosts { get; set; }

        public bool AutoSyncEnabled { get; set; } = true;

        [Range(1, 1440)]
        public int FetchIntervalMinutes { get; set; } = 15;

        public bool WriteEnabled { get; set; }
    }
}
