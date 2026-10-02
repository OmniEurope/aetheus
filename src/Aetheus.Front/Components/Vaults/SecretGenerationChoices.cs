// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Components.Vaults;

/// <summary>
/// Recette R-437: the menu of the Generate button, beside a secret value field (adding a secret,
/// updating or rotating one). A click on Generate itself applies the recipe of the secret's key
/// (<see cref="VaultSecretGenerator.Generate(string?)"/>); the menu offers the other ways, more may
/// follow. Each choice is a label key and the generator it runs.
/// </summary>
internal static class SecretGenerationChoices
{
    internal sealed record Choice(string LabelKey, Func<string> Generate);

    internal static IReadOnlyList<Choice> All { get; } =
    [
        new("SecretGenerateCharacters32", () => VaultSecretGenerator.GenerateCharacters(32)),
        new("SecretGenerateCharacters64", () => VaultSecretGenerator.GenerateCharacters(64)),
        new("SecretGenerateHex32Bytes", () => VaultSecretGenerator.Generate(
            new VaultSecretGenerator.Profile(string.Empty, 32, VaultSecretGenerator.Encoding.Hex, "32 random bytes, hexadecimal")))
    ];
}
