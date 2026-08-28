// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Shared;

public partial class EntityAuditTrail
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;
    [Parameter, EditorRequired] public int EntityId { get; set; }

    private List<AuditLogDto> _logs = [];
    private int _count;
    private bool _loading;

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        var (page, pageSize) = args.ToPageRequest(25);
        var (sortBy, sortDescending) = args.ToSortRequest(nameof(AuditLogDto.Timestamp), fallbackDescending: true);
        _loading = true;
        try
        {
            var result = await Api.Monitoring.GetAuditLogsAsync(page, pageSize, entityType: EntityType, entityId: EntityId,
                sortBy: sortBy, sortDescending: sortDescending);
            _logs = result.Items;
            _count = result.TotalCount;
        }
        finally
        {
            _loading = false;
        }
    }

    private string ActionText(string action)
    {
        var localized = L[$"AuditAction_{action}"];
        return localized.ResourceNotFound ? action : localized.Value;
    }

    private Task ShowDetailsAsync(AuditLogDto log) =>
        Dialog.OpenAsync<Aetheus.Front.Pages.Audit.AuditDetailDialog>(L["AuditEntryDetail"],
            new Dictionary<string, object?> { ["Log"] = log },
            new DialogOptions
            {
                Width = "700px",
                CloseDialogOnOverlayClick = true,
                AutoFocusFirstElement = false
            });

    private static BadgeStyle GetActionBadge(string action) => action switch
    {
        var value when value.Contains("Created", StringComparison.OrdinalIgnoreCase) => BadgeStyle.Success,
        var value when value.Contains("Deleted", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Failed", StringComparison.OrdinalIgnoreCase) => BadgeStyle.Danger,
        var value when value.Contains("Updated", StringComparison.OrdinalIgnoreCase) => BadgeStyle.Info,
        _ => BadgeStyle.Light
    };
}
