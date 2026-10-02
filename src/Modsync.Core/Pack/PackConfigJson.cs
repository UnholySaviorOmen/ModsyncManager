// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Pack;

public static class PackConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(PackConfig config)
        => JsonSerializer.Serialize(config, Options);

    public static PackConfig Deserialize(string json)
        => JsonSerializer.Deserialize<PackConfig>(json, Options)
           ?? throw new JsonException("PackConfig is null");

    public static async Task<PackConfig> LoadAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PackConfig>(fs, Options, ct)
               ?? throw new JsonException("PackConfig is null");
    }

    public static async Task SaveAsync(string path, PackConfig config, CancellationToken ct)
    {
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, config, Options, ct);
    }
}
