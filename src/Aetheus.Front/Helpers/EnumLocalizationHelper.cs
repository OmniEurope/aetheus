// SPDX-License-Identifier: EUPL-1.2
using Microsoft.Extensions.Localization;

namespace Aetheus.Front.Helpers;

public static class EnumLocalizationHelper
{
    public static string Localize<T>(this IStringLocalizer localizer, T value) where T : struct, Enum
        => localizer[$"Enum_{typeof(T).Name}_{value}"];
}
