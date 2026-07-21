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

    private static readonly IReadOnlyDictionary<string, string> NonRadzenDialogComponents =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("Layout", "ConnectionLostDialog.razor")] = "Framework connection-loss overlay.",
            [Path.Combine("Shared", "ConfirmDialog.razor")] = "Custom overlay with Escape and explicit cancel callbacks.",
            [Path.Combine("Shared", "PermissionsChangedDialog.razor")] = "Blocking acknowledgement dialog.",
            [Path.Combine("Shared", "WizardDialog.razor")] = "Custom overlay with its own Escape and cancel contract."
        };

    [Fact]
    public void DialogOptions_StayDismissibleUnlessDocumented()
    {
        var frontDir = FrontDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(frontDir, "*.*", SearchOption.AllDirectories)
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

        foreach (var file in Directory.EnumerateFiles(frontDir, "*Dialog.razor", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(frontDir, file);
            if (NonRadzenDialogComponents.ContainsKey(relative)) continue;

            var source = File.ReadAllText(file);
            var codeBehind = File.Exists(file + ".cs") ? File.ReadAllText(file + ".cs") : string.Empty;
            var combined = source + codeBehind;
            var hasSharedAction = source.Contains("<Aetheus.Front.Shared.DialogFooter", StringComparison.Ordinal)
                                  || source.Contains("<DialogFooter", StringComparison.Ordinal)
                                  || source.Contains("<Aetheus.Front.Shared.DialogCloseButton", StringComparison.Ordinal)
                                  || source.Contains("<DialogCloseButton", StringComparison.Ordinal);
            var hasExplicitAction = source.Contains("<RadzenButton", StringComparison.Ordinal)
                                    && (combined.Contains("Dialog.Close", StringComparison.Ordinal)
                                        || combined.Contains("DialogService.Close", StringComparison.Ordinal));

            if (!hasSharedAction && !hasExplicitAction) violations.Add(relative);
        }

        Assert.True(violations.Count == 0,
            "Every DialogService component must expose DialogFooter, DialogCloseButton, or an explicit "
            + "RadzenButton wired to Dialog.Close:\n  " + string.Join("\n  ", violations));
    }

    private static string FrontDirectory() => Path.Combine(FindRepoRoot(), "src", "Aetheus.Front");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(typeof(DialogConventionGuardTests).Assembly.Location)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aetheus.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (Aetheus.slnx).");
    }
}
