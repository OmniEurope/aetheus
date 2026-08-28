// SPDX-License-Identifier: EUPL-1.2
using Cronos;

namespace Aetheus.Front.Pages.Servers.ServerDetailSections;

public partial class ServerCronSection
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private NotifyHelper Toast { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;
    [Inject] private DialogService Dialog { get; set; } = default!;

    [Parameter, EditorRequired] public ServerDetailDto Server { get; set; } = default!;
    [Parameter, EditorRequired] public int ServerId { get; set; }

    private bool _actionRunning;
    private string _jobSearch = string.Empty;

    private bool _confirmVisible;
    private string _confirmTitle = string.Empty;
    private string _confirmMessage = string.Empty;
    private Func<Task>? _confirmAction;

    private CronDataDto Cron => Server.Cron;

    internal static (bool IsValid, string? NextRun, string? Error) ValidateCronExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return (false, null, null);
        try
        {
            // Standard 5-field only: /etc/cron.d entries are 5-field, and a 6-field (seconds) schedule is
            // rejected at save (CronValidation.IsValidScheduleSyntax), so the preview must mirror that.
            var cron = CronExpression.Parse(expression, CronFormat.Standard);
            var next = cron.GetNextOccurrence(DateTime.UtcNow);
            return (true, next?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), null);
        }
        catch (CronFormatException ex)
        {
            return (false, null, ex.Message);
        }
    }

    private List<CronJobDto> FilteredJobs => string.IsNullOrWhiteSpace(_jobSearch)
        ? Cron.Jobs
        : Cron.Jobs.Where(j =>
            j.Command.Contains(_jobSearch, StringComparison.OrdinalIgnoreCase) ||
            j.User.Contains(_jobSearch, StringComparison.OrdinalIgnoreCase) ||
            j.Schedule.Contains(_jobSearch, StringComparison.OrdinalIgnoreCase)).ToList();

    private async Task OpenCreateDialog()
    {
        var result = await Dialog.OpenAsync<CronJobDialog>(
            L["NewCronJob"],
            new Dictionary<string, object?>
            {
                { "JobId", string.Empty },
                { "User", string.Empty },
                { "Schedule", string.Empty },
                { "Command", string.Empty }
            },
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });

        if (result is CronJobSaveRequest request)
            await SaveJobAsync(request);
    }

    private async Task OpenEditDialog(CronJobDto job)
    {
        var result = await Dialog.OpenAsync<CronJobDialog>(
            L["EditCronJob"],
            new Dictionary<string, object?>
            {
                { "JobId", job.Id },
                { "User", job.User },
                { "Schedule", job.Schedule },
                { "Command", job.Command }
            },
            new DialogOptions { Width = "32rem", CloseDialogOnOverlayClick = true, AutoFocusFirstElement = false });

        if (result is CronJobSaveRequest request)
            await SaveJobAsync(request);
    }

    private async Task SaveJobAsync(CronJobSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Schedule) || string.IsNullOrWhiteSpace(request.Command))
        {
            Toast.Error(L["Error"], L["CronJobInvalid"]);
            return;
        }

        _actionRunning = true;
        var success = await Api.ServerTools.SaveCronJobAsync(ServerId, request);
        if (success)
            Toast.Success(L["TaskQueued"]);
        else
            Toast.Error(L["Error"], L["ActionFailed"]);
        _actionRunning = false;
    }

    private async Task DeleteJobAsync(CronJobDto job)
    {
        _actionRunning = true;
        var success = await Api.ServerTools.DeleteCronJobAsync(ServerId, new CronJobDeleteRequest { Id = job.Id, User = job.User });
        if (success)
            Toast.Success(L["TaskQueued"]);
        else
            Toast.Error(L["Error"], L["ActionFailed"]);
        _actionRunning = false;
    }

    private void ShowConfirm(string title, string message, Func<Task> action)
    {
        _confirmTitle = title;
        _confirmMessage = message;
        _confirmAction = action;
        _confirmVisible = true;
    }

    private async Task ConfirmAccepted()
    {
        _confirmVisible = false;
        if (_confirmAction is not null)
            await _confirmAction();
    }
}
