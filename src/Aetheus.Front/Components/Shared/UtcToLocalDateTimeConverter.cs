// SPDX-License-Identifier: EUPL-1.2
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aetheus.Front.Components.Shared;

/// <summary>
/// Reads UTC DateTimes from the wire (the backend timestamps everything in UTC,
/// per the CLAUDE.md TimeProvider convention) and surfaces them in the browser's
/// LOCAL timezone, so DataGrid / ToString("g") show correct local clock time
/// without each call site having to remember ToLocalTime(). Inverse on write:
/// Local values are sent as UTC so round-trips stay UTC-canonical on the wire.
/// </summary>
internal sealed class UtcToLocalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dt = reader.GetDateTime();
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt.ToLocalTime(),
            // No 'Z' on the wire - treat as UTC per backend convention, then localise.
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(),
            _ => dt
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        writer.WriteStringValue(utc);
    }
}

internal sealed class NullableUtcToLocalDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var dt = reader.GetDateTime();
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(),
            _ => dt
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        var utc = value.Value.Kind == DateTimeKind.Utc ? value.Value : value.Value.ToUniversalTime();
        writer.WriteStringValue(utc);
    }
}
