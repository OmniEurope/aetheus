// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Servers.AgentWizard;

/// <summary>
/// The toast after an OmniCodeBlock copy in the AddAgent wizard, the same as <see cref="ClipboardService"/>
/// gives elsewhere: success when the clipboard took the text, the "clipboard unavailable" warning otherwise.
/// </summary>
internal static class WizardCopyFeedback
{
    public static void Report(NotifyHelper toast, IStringLocalizer<AppStrings> localizer, bool copied)
    {
        if (copied)
            toast.Notify(OmniSeverity.Success, "Copied", localizer["CopiedToClipboard"].Value);
        else
            toast.Warning("CopyFailed", "ClipboardUnavailable");
    }
}
