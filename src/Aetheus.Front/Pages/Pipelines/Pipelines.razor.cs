// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Pipelines;

public partial class Pipelines : IDisposable
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private BreadcrumbService Breadcrumb { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    [Parameter, SupplyParameterFromQuery(Name = "templateId")]
    public int? TemplateId { get; set; }

    private bool _canReadPipelines;
    private bool _canReadTemplates;
    private IReadOnlyList<string> TabSlugs => _canReadPipelines && _canReadTemplates
        ? ["used", "models"]
        : _canReadPipelines ? ["used"] : ["models"];

    protected override void OnInitialized()
    {
        var path = Nav.ToBaseRelativePath(Nav.Uri).Split('?', 2)[0].TrimEnd('/');
        if (path.Equals("templates", StringComparison.OrdinalIgnoreCase))
        {
            Nav.NavigateTo("/pipelines?tab=models", replace: true);
            return;
        }
        if (path.Equals("pipelines/fleet", StringComparison.OrdinalIgnoreCase))
        {
            Nav.NavigateTo("/pipelines", replace: true);
            return;
        }

        Breadcrumb.Set(new BreadcrumbItem(L["Pipelines"]));
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshPermissions();
    }

    private void OnPermissionsChanged()
    {
        RefreshPermissions();
        _ = InvokeAsync(StateHasChanged);
    }

    private void RefreshPermissions()
    {
        _canReadPipelines = Permissions.CanReadAny(ResourceType.Pipeline);
        _canReadTemplates = Permissions.CanReadAny(ResourceType.PipelineTemplate);
    }

    public void Dispose() => Permissions.OnPermissionsChanged -= OnPermissionsChanged;
}
