// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Hashing;

internal sealed class XxHash64ValueJsonConverter : JsonConverter<XxHash64Value>
{
    public override XxHash64Value Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString() ?? throw new JsonException("Hash is null");
        return XxHash64Value.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, XxHash64Value value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
