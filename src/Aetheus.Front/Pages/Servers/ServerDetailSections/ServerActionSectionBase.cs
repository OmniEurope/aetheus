// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public abstract class ServerActionSectionBase : ComponentBase
{
    [Inject] protected NotifyHelper Toast { get; set; } = default!;
    [Inject] protected IStringLocalizer<AppStrings> L { get; set; } = default!;

    protected bool _actionRunning;
    protected bool _confirmVisible;
    protected string _confirmTitle = string.Empty;
    protected string _confirmMessage = string.Empty;
    protected Func<Task>? _confirmAction;

    protected async Task ExecuteServerActionAsync(Func<Task<ApiStatus>> action)
    {
        _actionRunning = true;
        try
        {
            if ((await action()).Success) Toast.Success(L["TaskQueued"]);
            else Toast.Error(L["ActionFailed"]);
        }
        catch
        {
            Toast.Error(L["ActionFailed"]);
        }
        finally
        {
            _actionRunning = false;
        }
    }

    protected void ShowConfirm(string title, string message, Func<Task> action)
    {
        _confirmTitle = title;
        _confirmMessage = message;
        _confirmAction = action;
        _confirmVisible = true;
    }

    protected async Task ConfirmAccepted()
    {
        _confirmVisible = false;
        if (_confirmAction is not null) await _confirmAction();
    }

    protected void ConfirmCancelled()
    {
        _confirmVisible = false;
        _confirmAction = null;
    }
}
