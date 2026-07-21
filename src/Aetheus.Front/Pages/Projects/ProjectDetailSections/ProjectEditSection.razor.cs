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

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public partial class ProjectEditSection
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    [Parameter, EditorRequired] public ProjectDetailDto? Project { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }

    private ProjectModel _model = new();
    private bool _saving;
    private List<object> _statusOptions = [];

    // DefaultBranch dropdown: the project's internal repo branches (empty => fall back to a free-text box).
    private List<string> _branches = [];
    private bool _branchesLoaded;
    private int? _branchesProjectId;

    protected override void OnParametersSet()
    {
        _statusOptions =
        [
            new { Text = L["Active"].Value, Value = ProjectStatus.Active },
            new { Text = L["Archived"].Value, Value = ProjectStatus.Archived }
        ];

        if (Project is not null)
        {
            _model = new ProjectModel
            {
                Name = Project.Name,
                Description = Project.Description,
                RepositoryUrl = Project.RepositoryUrl ?? string.Empty,
                DefaultBranch = Project.DefaultBranch ?? string.Empty,
                Status = Project.Status,
                TagsRaw = string.Join(", ", Project.Tags),
                ArtifactRetentionDays = Project.ArtifactRetentionDays,
                ArtifactLatestRetentionDays = Project.ArtifactLatestRetentionDays,
                ReleaseNumberingPattern = Project.ReleaseNumberingPattern ?? string.Empty
            };
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Project is null || _branchesProjectId == Project.Id) return;
        _branchesProjectId = Project.Id;
        await LoadBranchesAsync();
    }

    private async Task LoadBranchesAsync()
    {
        _branches = [];
        // Best-effort: the branch list only enriches the DefaultBranch picker. A repo without an
        // internal git repo (external-only / not yet cloned) or a transient failure just falls back
        // to the free-text box - never blocks editing.
        try
        {
            var repos = await Api.GetGitReposAsync(Project!.Id);
            var repo = repos.FirstOrDefault();
            if (repo is not null)
                _branches = (await Api.GetGitBranchesAsync(repo.Id)).Select(b => b.Name).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _branches = [];
        }

        _branchesLoaded = _branches.Count > 0;
        _model.DefaultBranch = ResolveDefaultBranch(_branches, _model.DefaultBranch);
        StateHasChanged();
    }

    /// <summary>Case-insensitive default-branch resolution: keep an existing value (normalised to the
    /// repo's actual branch casing) when it still exists, otherwise prefer <c>main</c>, then
    /// <c>master</c>, then the first branch. With no branches, keep the current value or fall back to
    /// <c>main</c>.</summary>
    internal static string ResolveDefaultBranch(IReadOnlyList<string> branches, string? current)
    {
        if (branches.Count == 0)
            return string.IsNullOrWhiteSpace(current) ? "main" : current;

        if (!string.IsNullOrWhiteSpace(current))
        {
            var match = branches.FirstOrDefault(b => b.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return branches.FirstOrDefault(b => b.Equals("main", StringComparison.OrdinalIgnoreCase))
            ?? branches.FirstOrDefault(b => b.Equals("master", StringComparison.OrdinalIgnoreCase))
            ?? branches[0];
    }

    private async Task OnSubmit()
    {
        _saving = true;
        var tags = _model.TagsRaw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var updated = await Api.UpdateProjectAsync(Project!.Id, new UpdateProjectRequest
        {
            Name = _model.Name,
            Description = _model.Description,
            RepositoryUrl = string.IsNullOrWhiteSpace(_model.RepositoryUrl) ? null : _model.RepositoryUrl,
            DefaultBranch = string.IsNullOrWhiteSpace(_model.DefaultBranch) ? null : _model.DefaultBranch,
            Status = _model.Status,
            Tags = tags,
            ArtifactRetentionDays = _model.ArtifactRetentionDays,
            ArtifactLatestRetentionDays = _model.ArtifactLatestRetentionDays,
            ReleaseNumberingPattern = string.IsNullOrWhiteSpace(_model.ReleaseNumberingPattern) ? null : _model.ReleaseNumberingPattern
        });

        if (updated is not null)
        {
            Toast.Success("Saved", "ProjectUpdated");
            await OnSaved.InvokeAsync();
        }
        else
        {
            Toast.Error("Error", "SaveFailed");
        }
        _saving = false;
    }

    private async Task OnDelete()
    {
        var confirmed = await Dialog.Confirm(L["DeleteProjectConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var success = await Api.DeleteProjectAsync(Project!.Id);
        if (success)
        {
            Nav.NavigateTo("/projects");
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
    }

    internal class ProjectModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [StringLength(100, ErrorMessage = "Name must be 100 characters or fewer.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        public string RepositoryUrl { get; set; } = string.Empty;
        public string DefaultBranch { get; set; } = string.Empty;
        public ProjectStatus Status { get; set; } = ProjectStatus.Active;
        public string TagsRaw { get; set; } = string.Empty;

        [Range(1, 3650, ErrorMessage = "Retention must be between 1 and 3650 days.")]
        public int? ArtifactRetentionDays { get; set; }

        [Range(1, 3650, ErrorMessage = "Retention must be between 1 and 3650 days.")]
        public int? ArtifactLatestRetentionDays { get; set; }

        [StringLength(100)]
        public string ReleaseNumberingPattern { get; set; } = string.Empty;
    }
}
