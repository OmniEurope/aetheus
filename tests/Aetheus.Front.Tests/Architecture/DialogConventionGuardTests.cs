// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests.Architecture;

public class DialogConventionGuardTests
{
    private static readonly IReadOnlyDictionary<string, string> NonDismissibleExceptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("Layout", "NavMenu.razor.cs")] =
                "PermissionsChangedDialog requires acknowledgement because the active permission model is stale."
        };

    private static readonly IReadOnlyDictionary<string, string> NonLibraryDialogComponents =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("Layout", "ConnectionLostDialog.razor")] = "Framework connection-loss overlay.",
            [Path.Combine("Components", "Shared", "PermissionsChangedDialog.razor")] = "Blocking acknowledgement dialog.",
            [Path.Combine("Components", "Shared", "WizardDialog.razor")] = "Custom overlay with its own Escape and cancel contract."
        };

    /// <summary>
    /// A dialog that focuses its first form field on open makes a screen reader announce
    /// that field before the dialog's own title - the user hears "Name, edit" with no idea what they
    /// are naming. Every dialog therefore opts out, so focus lands on the dialog itself.
    /// </summary>
    [Fact]
    public void EveryDialogOptions_DisablesFirstElementAutoFocus()
    {
        var frontDir = FrontDirectory();
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*.*")
                     .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            var declared = System.Text.RegularExpressions.Regex.Matches(source, @"new OmniDialogOptions\b").Count;
            if (declared == 0) continue;

            var optedOut = System.Text.RegularExpressions.Regex
                .Matches(source, @"AutoFocusFirstElement\s*=\s*false").Count;
            if (optedOut < declared)
                violations.Add($"{Path.GetRelativePath(frontDir, file)} ({declared} OmniDialogOptions, {optedOut} opted out)");
        }

        Assert.True(violations.Count == 0,
            "Every OmniDialogOptions must set AutoFocusFirstElement = false so the dialog title is "
            + "announced before its first field (docs/contracts/ui-patterns.md):\n  "
            + string.Join("\n  ", violations.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void DialogOptions_StayDismissibleUnlessDocumented()
    {
        var frontDir = FrontDirectory();
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*.*")
                     .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                    || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
        {
            var relative = Path.GetRelativePath(frontDir, file);
            var source = File.ReadAllText(file);
            if (!source.Contains("ShowClose = false", StringComparison.Ordinal)
                && !source.Contains("CloseDialogOnEsc = false", StringComparison.Ordinal))
            {
                continue;
            }

            if (NonDismissibleExceptions.ContainsKey(relative)
                && source.Contains("STD-DIALOG exception:", StringComparison.Ordinal))
            {
                continue;
            }

            violations.Add(relative);
        }

        Assert.True(violations.Count == 0,
            "Dialogs must keep the close affordance and Escape behavior. A blocking exception requires "
            + "an entry in the guard and an inline 'STD-DIALOG exception:' rationale:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void DialogComponents_ExposeAnExplicitCancelOrCloseAction()
    {
        var frontDir = FrontDirectory();
        var violations = new List<string>();

        foreach (var file in RepositoryScan.Enumerate(frontDir, "*Dialog.razor"))
        {
            var relative = Path.GetRelativePath(frontDir, file);
            if (NonLibraryDialogComponents.ContainsKey(relative)) continue;

            var source = File.ReadAllText(file);
            var codeBehind = File.Exists(file + ".cs") ? File.ReadAllText(file + ".cs") : string.Empty;
            var combined = source + codeBehind;
            var hasSharedAction = source.Contains("<Aetheus.Front.Components.Shared.DialogFooter", StringComparison.Ordinal)
                                  || source.Contains("<DialogFooter", StringComparison.Ordinal)
                                  || source.Contains("<Aetheus.Front.Components.Servers.DialogCloseButton", StringComparison.Ordinal)
                                  || source.Contains("<DialogCloseButton", StringComparison.Ordinal);
            var hasExplicitAction = source.Contains("<OmniButton", StringComparison.Ordinal)
                                    && (combined.Contains("Dialog.Close", StringComparison.Ordinal)
                                        || combined.Contains("OmniDialogService.Close", StringComparison.Ordinal));

            if (!hasSharedAction && !hasExplicitAction) violations.Add(relative);
        }

        Assert.True(violations.Count == 0,
            "Every OmniDialogService component must expose DialogFooter, DialogCloseButton, or an explicit "
            + "OmniButton wired to Dialog.Close:\n  " + string.Join("\n  ", violations));
    }

    private static string FrontDirectory() => Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");

    private static string FindRepoRoot() => Aetheus.Front.Tests.Architecture.RepositoryScan.Root;
}
