// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Radzen;

namespace Aetheus.Front.Pages.Projects;

public partial class ProjectFormDialog
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private ConfirmHelper Confirm { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;

    /// <summary>Existing project to edit; null opens the dialog in create mode.</summary>
    [Parameter] public ProjectDetailDto? Project { get; set; }

    private ProjectFormModel _model = new();
    private bool _saving;
    private List<object> _statusOptions = [];
    private bool _isNew => Project is null;

    /// <summary>Shared Radzen dialog options for opening the project form (create or edit).</summary>
    public static DialogOptions DialogOptions() => new()
    {
        Width = "min(920px, 94vw)",
        CloseDialogOnOverlayClick = false,
        ShowClose = true
    };

    protected override void OnInitialized()
    {
        _statusOptions =
        [
            new { Text = L["Active"].Value, Value = ProjectStatus.Active },
            new { Text = L["Archived"].Value, Value = ProjectStatus.Archived }
        ];

        if (Project is not null)
        {
            _model = new ProjectFormModel
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try { await Js.InvokeVoidAsync("Aetheus.focusById", "project-form-name"); }
            catch { /* best effort */ }
        }
    }

    private async Task OnSubmit()
    {
        _saving = true;
        var tags = _model.TagsRaw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var result = _isNew
            ? await Api.CreateProjectAsync(new CreateProjectRequest
            {
                Name = _model.Name,
                Description = _model.Description,
                RepositoryUrl = Nullify(_model.RepositoryUrl),
                DefaultBranch = Nullify(_model.DefaultBranch),
                Tags = tags,
                ArtifactRetentionDays = _model.ArtifactRetentionDays,
                ArtifactLatestRetentionDays = _model.ArtifactLatestRetentionDays,
                ReleaseNumberingPattern = Nullify(_model.ReleaseNumberingPattern)
            })
            : await Api.UpdateProjectAsync(Project!.Id, new UpdateProjectRequest
            {
                Name = _model.Name,
                Description = _model.Description,
                RepositoryUrl = Nullify(_model.RepositoryUrl),
                DefaultBranch = Nullify(_model.DefaultBranch),
                Status = _model.Status,
                Tags = tags,
                ArtifactRetentionDays = _model.ArtifactRetentionDays,
                ArtifactLatestRetentionDays = _model.ArtifactLatestRetentionDays,
                ReleaseNumberingPattern = Nullify(_model.ReleaseNumberingPattern)
            });
        _saving = false;

        if (result is not null)
        {
            Toast.Success(_isNew ? "Created" : "Saved", _isNew ? "ProjectCreated" : "ProjectUpdated");
            Dialog.Close(result);
        }
        else
        {
            Toast.Error("Error", "SaveFailed");
        }
    }

    private async Task OnDelete()
    {
        if (Project is null) return;

        var confirmed = await Confirm.ConfirmDeleteAsync("DeleteProjectConfirm", "Delete");
        if (confirmed != true) return;

        var success = await Api.DeleteProjectAsync(Project.Id);
        if (success)
        {
            Dialog.Close();
            Nav.NavigateTo("/projects");
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
    }

    private static string? Nullify(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    internal sealed class ProjectFormModel
    {
        // Default DataAnnotations messages - LocalizedDataAnnotationsValidator translates them.
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        public string RepositoryUrl { get; set; } = string.Empty;
        public string DefaultBranch { get; set; } = string.Empty;
        public ProjectStatus Status { get; set; } = ProjectStatus.Active;
        public string TagsRaw { get; set; } = string.Empty;

        [Range(1, 3650)]
        public int? ArtifactRetentionDays { get; set; }

        [Range(1, 3650)]
        public int? ArtifactLatestRetentionDays { get; set; }

        [StringLength(100)]
        public string ReleaseNumberingPattern { get; set; } = string.Empty;
    }
}
