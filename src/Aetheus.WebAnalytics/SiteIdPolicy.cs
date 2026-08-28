// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.WebAnalytics;

internal static class SiteIdPolicy
{
    public static bool IsValid(string value) =>
        value.Length is >= 1 and <= 64
        && value.All(character => character is >= 'a' and <= 'z'
                                  or >= '0' and <= '9'
                                  or '.' or '-');
}
