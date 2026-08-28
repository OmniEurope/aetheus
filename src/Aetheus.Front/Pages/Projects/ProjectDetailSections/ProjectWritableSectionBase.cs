// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Projects.ProjectDetailSections;

public abstract class ProjectWritableSectionBase : ComponentBase, IDisposable
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] protected DialogService Dialog { get; set; } = default!;
    [Inject] protected PermissionService Permissions { get; set; } = default!;
    [Inject] protected NotifyHelper Toast { get; set; } = default!;

    [Parameter] public int ProjectId { get; set; }

    protected bool _loading = true;
    protected bool _canWrite;

    protected override void OnInitialized()
    {
        Permissions.OnPermissionsChanged += OnPermissionsChanged;
        RefreshCanWrite();
    }

    protected override Task OnParametersSetAsync() => LoadAsync();

    protected abstract Task LoadAsync();

    private void OnPermissionsChanged()
    {
        RefreshCanWrite();
        _ = InvokeAsync(StateHasChanged);
    }

    private void RefreshCanWrite() => _canWrite = Permissions.CanWrite(ResourceType.Project);

    public void Dispose() => Permissions.OnPermissionsChanged -= OnPermissionsChanged;
}
