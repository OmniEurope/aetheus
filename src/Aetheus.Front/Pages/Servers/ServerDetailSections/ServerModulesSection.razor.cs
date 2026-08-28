// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerModulesSection
{
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public int ServerId { get; set; }

    private List<ServerModuleDto>? _modules;
    private int? _loadedServerId;
    private bool _addVisible;
    private bool _addSaving;
    private AddModuleModel _addModel = new();
    private static readonly ServerModuleType[] _moduleTypes = Enum.GetValues<ServerModuleType>();

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedServerId == ServerId) return;
        _loadedServerId = ServerId;
        var serverId = ServerId;
        try
        {
            var modules = await Api.Servers.GetServerModulesAsync(serverId);
            if (ServerId == serverId) _modules = modules;
        }
        catch (HttpRequestException)
        {
            if (ServerId == serverId) _modules = [];
        } // 401 on expired JWT - redirect handled by AuthProvider
    }

    private async Task AddModuleAsync(AddModuleModel model)
    {
        _addSaving = true;
        try
        {
            var result = await Api.Servers.CreateServerModuleAsync(ServerId, new CreateServerModuleRequest
            {
                Name = model.Name,
                Type = model.Type,
                Version = string.IsNullOrWhiteSpace(model.Version) ? null : model.Version
            });
            if (result is null)
            {
                Toast.Error("Error", "CreateFailed");
                return;
            }

            _modules!.Add(result);
            _addVisible = false;
            _addModel = new AddModuleModel();
            Toast.Success("Created", "ModuleCreated");
        }
        catch (HttpRequestException)
        {
            Toast.Error("Error", "CreateFailed");
        }
        finally
        {
            _addSaving = false;
        }
    }

    private sealed class AddModuleModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public ServerModuleType Type { get; set; }

        [StringLength(50)]
        public string Version { get; set; } = string.Empty;
    }

    private async Task DeleteModuleAsync(ServerModuleDto module)
    {
        var confirmed = await Dialog.Confirm(L["DeleteConfirm"].Value, L["Delete"].Value,
            new ConfirmOptions { OkButtonText = L["Delete"].Value, CancelButtonText = L["Cancel"].Value });
        if (confirmed != true) return;

        var success = await Api.Servers.DeleteServerModuleAsync(ServerId, module.Id);
        if (success)
        {
            _modules!.RemoveAll(m => m.Id == module.Id);
            Toast.Success("Deleted", "Deleted");
        }
        else
        {
            Toast.Error("Error", "DeleteFailed");
        }
    }

    private static BadgeStyle GetModuleStatusBadge(ServerModuleStatus status) => status switch
    {
        ServerModuleStatus.Active => BadgeStyle.Success,
        ServerModuleStatus.Inactive => BadgeStyle.Light,
        ServerModuleStatus.Error => BadgeStyle.Danger,
        _ => BadgeStyle.Warning
    };
}
