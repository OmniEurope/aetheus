// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Pipelines;

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
        Breadcrumb.Set(new BreadcrumbItem(L["Pipelines"]));
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshPermissions();

        // /templates and /pipelines/fleet are aliases of this very component, so the router reuses
        // the running instance and OnInitialized never runs again after the redirect. Redirecting
        // before initializing therefore left the hub with no permissions, no tabs and no breadcrumb
        // for good: a cold load of /templates rendered nothing but the page title (2026-09-20).
        var path = Nav.ToBaseRelativePath(Nav.Uri).Split('?', 2)[0].TrimEnd('/');
        if (path.Equals("templates", StringComparison.OrdinalIgnoreCase))
        {
            Nav.NavigateTo("/pipelines?tab=models", replace: true);
        }
        else if (path.Equals("pipelines/fleet", StringComparison.OrdinalIgnoreCase))
        {
            Nav.NavigateTo("/pipelines", replace: true);
        }
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
