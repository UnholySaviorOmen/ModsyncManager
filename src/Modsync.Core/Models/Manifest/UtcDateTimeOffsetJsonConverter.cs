// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Manifest;

/// <summary>
/// JSON-конвертер для DateTimeOffset в манифесте.
///
/// Сериализует всегда в UTC с суффиксом 'Z':
///     2026-09-16T21:30:17.310Z
///
/// Десериализует любой валидный ISO 8601 (с 'Z', с offset-ом, без того и другого),
/// приводит к UTC (Offset == 0).
/// </summary>
internal sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"Expected string for DateTimeOffset, got {reader.TokenType}.");
        }

        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s))
            throw new JsonException("DateTimeOffset string is empty.");

        // AssumeUniversal: строки без offset трактуются как UTC.
        // AdjustToUniversal: результат имеет Offset == 0.
        if (!DateTimeOffset.TryParse(
                s,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var result))
        {
            throw new JsonException($"Invalid DateTimeOffset: '{s}'.");
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        var utc = value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString(Format, CultureInfo.InvariantCulture));
    }
}
