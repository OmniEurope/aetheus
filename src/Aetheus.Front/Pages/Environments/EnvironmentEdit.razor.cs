// SPDX-License-Identifier: EUPL-1.2
using System.ComponentModel.DataAnnotations;
using Aetheus.Front.Helpers;
using Aetheus.Front.Layout;
using Aetheus.Front.Resources;
using Aetheus.Front.Services;
using Aetheus.Shared.DTOs;
using Aetheus.Shared.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Pages.Environments;

public partial class EnvironmentEdit
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private ProjectNavContextService ProjectNav { get; set; } = default!;
    [Inject] private ListCacheService Cache { get; set; } = default!;

    [Parameter] public int? Id { get; set; }

    [SupplyParameterFromQuery] public int? ProjectId { get; set; }

    private EnvironmentDto? _detail;
    private EnvironmentModel _model = new();
    private List<ProjectDto> _projects = [];
    private List<ServerDto> _servers = [];
    private bool _isNew => Id is null or 0;
    private bool _saving;
    private int? _previousId = int.MinValue;
    private int? _previousProjectId = int.MinValue;
    private int _loadGeneration;

    private List<EnvironmentTypeOption> _typeOptions = [];

    private readonly record struct EnvironmentTypeOption(string Text, EnvironmentType Value);

    protected override void OnInitialized()
    {
        _typeOptions = Enum.GetValues<EnvironmentType>()
            .Select(value => new EnvironmentTypeOption(L.Localize(value), value))
            .ToList();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Id == _previousId && ProjectId == _previousProjectId) return;
        _previousId = Id;
        _previousProjectId = ProjectId;
        var generation = Interlocked.Increment(ref _loadGeneration);

        _detail = null;
        _model = new EnvironmentModel();

        try
        {
            var projectsTask = Api.GetAllProjectsAsync();
            var serversTask = Api.GetAllServersAsync();
            await Task.WhenAll(projectsTask, serversTask);
            if (generation != _loadGeneration) return;
            _projects = await projectsTask;
            _servers = await serversTask;

            if (!_isNew)
            {
                _detail = await Api.GetEnvironmentAsync(Id!.Value);
                if (generation != _loadGeneration) return;
                if (_detail is not null)
                {
                    _model = new EnvironmentModel
                    {
                        Name = _detail.Name,
                        Description = _detail.Description,
                        Type = _detail.Type,
                        ProjectId = _detail.ProjectId,
                        RequireApproval = _detail.RequireApproval,
                        ApprovalTimeoutMinutes = _detail.ApprovalTimeoutMinutes,
                        ApprovalInstructions = _detail.ApprovalInstructions,
                        ServerIds = _detail.Servers.Select(s => s.ServerId).ToList()
                    };
                }
            }
            else if (ProjectId is > 0 && _projects.Any(p => p.Id == ProjectId.Value))
            {
                _model.ProjectId = ProjectId;
            }
        }
        catch (HttpRequestException) { } // 401 on expired JWT - redirect handled by AuthProvider

        ProjectNav.Set(_model.ProjectId);

        Breadcrumb.Set(
            new BreadcrumbItem(L["Environments"], "/environments"),
            new BreadcrumbItem(_isNew ? L["NewEnvironment"] : _detail?.Name ?? L["Environment"]));
    }

    private async Task OnSubmit()
    {
        _saving = true;
        if (_isNew)
        {
            var created = await Api.CreateEnvironmentAsync(new CreateEnvironmentRequest
            {
                Name = _model.Name,
                Description = _model.Description,
                Type = _model.Type,
                ProjectId = _model.ProjectId,
                RequireApproval = _model.RequireApproval,
                ApprovalTimeoutMinutes = _model.ApprovalTimeoutMinutes,
                ApprovalInstructions = _model.ApprovalInstructions,
                ServerIds = _model.ServerIds.ToList()
            });
            if (created is not null)
            {
                Toast.Success("Created", "EnvironmentCreated");
                Nav.NavigateTo($"/environments/{created.Id}");
            }
        }
        else
        {
            var updated = await Api.UpdateEnvironmentAsync(Id!.Value, new UpdateEnvironmentRequest
            {
                Name = _model.Name,
                Description = _model.Description,
                Type = _model.Type,
                ProjectId = _model.ProjectId,
                RequireApproval = _model.RequireApproval,
                ApprovalTimeoutMinutes = _model.ApprovalTimeoutMinutes,
                ApprovalInstructions = _model.ApprovalInstructions,
                ServerIds = _model.ServerIds.ToList()
            });
            if (updated is not null)
            {
                Toast.Success("Saved", "EnvironmentSaved");
                _detail = updated;
            }
        }
        _saving = false;
    }

    private async Task OnDelete()
    {
        var confirmed = await Dialog.Confirm(L["DeleteEnvironmentConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        await Api.DeleteEnvironmentAsync(Id!.Value);
        Cache.InvalidatePrefix("environments:"); // S-TECH-SWIV
        Nav.NavigateTo("/environments");
    }

    private static BadgeStyle TypeStyle(EnvironmentType type) => type switch
    {
        EnvironmentType.Production => BadgeStyle.Danger,
        EnvironmentType.Staging => BadgeStyle.Warning,
        EnvironmentType.Testing => BadgeStyle.Info,
        _ => BadgeStyle.Success
    };

    private class EnvironmentModel
    {
        // Default DataAnnotations messages - LocalizedDataAnnotationsValidator translates them
        // (Validation_Required / Validation_StringLength). A custom ErrorMessage would bypass that.
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        public EnvironmentType Type { get; set; } = EnvironmentType.Development;

        public int? ProjectId { get; set; }

        public bool RequireApproval { get; set; }

        [Range(1, 10080)]
        public int ApprovalTimeoutMinutes { get; set; } = 1440;

        [StringLength(1000)]
        public string? ApprovalInstructions { get; set; }

        public IEnumerable<int> ServerIds { get; set; } = [];
    }
}
