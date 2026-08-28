// SPDX-License-Identifier: EUPL-1.2

using Microsoft.AspNetCore.Components.Rendering;

namespace Aetheus.Front.Services;

public class NotifyHelper(
    NotificationService notification,
    IStringLocalizer<AppStrings> localizer,
    ClientErrorReporter? reporter = null,
    AuthStateProvider? auth = null)
{
    public void Success(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Success, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Error(string titleKey, string messageKey, params object[] args) =>
        ErrorRaw(localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Info(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Info, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Warning(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Warning, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Notify(NotificationSeverity severity, string titleKey, string rawMessage)
    {
        if (severity == NotificationSeverity.Error)
        {
            ErrorRaw(localizer[titleKey].Value, rawMessage);
            return;
        }
        notification.Notify(severity, localizer[titleKey].Value, rawMessage);
    }

    private string FormatMessage(string messageKey, object[] args) =>
        args.Length > 0
            ? string.Format(localizer[messageKey].Value, args)
            : localizer[messageKey].Value;

    public void Success(string summaryKey) =>
        notification.Notify(NotificationSeverity.Success, localizer[summaryKey].Value);

    public void Error(string summaryKey) =>
        ErrorRaw(localizer[summaryKey].Value, string.Empty);

    public void ErrorRaw(
        string summary,
        string detail,
        string? correlationId = null,
        bool reportClientError = true)
    {
        correlationId ??= Guid.NewGuid().ToString("N");
        if (reportClientError)
            reporter?.Report(
                correlationId,
                summary,
                string.IsNullOrWhiteSpace(detail) ? summary : detail);

        var message = new NotificationMessage
        {
            Severity = NotificationSeverity.Error,
            Summary = summary,
            Detail = detail,
            Duration = 7000
        };
        if (auth?.IsAdmin == true)
            message.DetailContent = BuildDetailContent(detail, correlationId);
        notification.Notify(message);
    }

    private RenderFragment<NotificationService> BuildDetailContent(string detail, string correlationId) => _ => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddContent(1, detail);
        builder.CloseElement();
        builder.AddContent(2, " ");
        builder.OpenElement(3, "a");
        builder.AddAttribute(4, "href", $"/admin/system-logs?search={Uri.EscapeDataString(correlationId)}");
        builder.AddAttribute(5, "class", "notification-log-link");
        builder.AddContent(6, localizer["Logs"]);
        builder.CloseElement();
    };
}
