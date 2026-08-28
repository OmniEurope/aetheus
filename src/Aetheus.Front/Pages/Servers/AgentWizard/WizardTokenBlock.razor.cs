// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Pages.Servers.AgentWizard;

public partial class WizardTokenBlock
{
    [Parameter] public string Token { get; set; } = string.Empty;

    [Inject] private ClipboardService Clipboard { get; set; } = default!;
    [Inject] private IStringLocalizer<AppStrings> L { get; set; } = default!;

    private string MaskedToken => Mask(Token);

    // S-DES-15: accent the literal <TOKEN> placeholder shown before a real token is issued.
    private bool IsPlaceholder => Token.StartsWith('<');

    private Task CopyAsync() => Clipboard.CopyAsync(Token, L["CopiedToClipboard"]);

    private static string Mask(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length <= 10) return token;
        return $"{token[..5]}...{token[^5..]}";
    }
}
