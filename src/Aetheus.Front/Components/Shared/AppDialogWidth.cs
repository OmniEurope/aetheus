// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Components.Shared;

/// <summary>
/// The widths a dialog opened through <see cref="AppDialogs"/> can take. OE's dialog is 40rem wide at
/// most; the others are rules of <c>app.css</c> on <c>.omni-dialog:has(.aetheus-dialog--…)</c>
/// (PLAN-008 verification, OE-13). They stand for the <c>OmniDialogOptions.Width</c> values the
/// front used before the migration:
/// <list type="bullet">
/// <item><see cref="Narrow"/> (32rem): 400 to 500 px, and 32rem;</item>
/// <item><see cref="Default"/> (40rem, OE's own): 520 to 640 px, 38rem and 40rem;</item>
/// <item><see cref="Wide"/> (52rem): 700 to 820 px, 42rem, 50rem and min(52rem, 96vw);</item>
/// <item><see cref="ExtraWide"/> (60rem): 900 and 920 px, min(920px, 94vw), and the one 90vw dialog,
/// which is narrower than before on a screen wider than 66rem.</item>
/// </list>
/// </summary>
public enum AppDialogWidth
{
    Default,
    Narrow,
    Wide,
    ExtraWide
}
