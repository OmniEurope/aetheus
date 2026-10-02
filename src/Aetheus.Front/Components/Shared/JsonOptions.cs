// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Shared JsonSerializerOptions for every API deserialisation in the WASM client.
/// Matches the implicit Web defaults <c>ReadFromJsonAsync</c> uses by default
/// (camelCase, case-insensitive) and layers on the UTC→Local DateTime converters
/// so timestamps appear in the user's browser timezone without per-site fixes.
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new UtcToLocalDateTimeConverter(),
            new NullableUtcToLocalDateTimeConverter(),
            new JsonStringEnumConverter()
        }
    };
}
