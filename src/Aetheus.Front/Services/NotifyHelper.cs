// SPDX-License-Identifier: EUPL-1.2
using Aetheus.Front.Resources;
using Microsoft.Extensions.Localization;
using Radzen;

namespace Aetheus.Front.Services;

public class NotifyHelper(NotificationService notification, IStringLocalizer<AppStrings> localizer)
{
    public void Success(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Success, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Error(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Error, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Info(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Info, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Warning(string titleKey, string messageKey, params object[] args) =>
        notification.Notify(NotificationSeverity.Warning, localizer[titleKey].Value, FormatMessage(messageKey, args));

    public void Notify(NotificationSeverity severity, string titleKey, string rawMessage) =>
        notification.Notify(severity, localizer[titleKey].Value, rawMessage);

    private string FormatMessage(string messageKey, object[] args) =>
        args.Length > 0
            ? string.Format(localizer[messageKey].Value, args)
            : localizer[messageKey].Value;

    public void Success(string summaryKey) =>
        notification.Notify(NotificationSeverity.Success, localizer[summaryKey].Value);

    public void Error(string summaryKey) =>
        notification.Notify(NotificationSeverity.Error, localizer[summaryKey].Value);
}
