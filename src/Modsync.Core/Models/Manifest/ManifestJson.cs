// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modsync.Core.Models.Manifest;

public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new UtcDateTimeOffsetJsonConverter() },
    };

    public static string Serialize(ModlistManifest manifest)
        => JsonSerializer.Serialize(manifest, Options);

    public static ModlistManifest Deserialize(string json)
        => JsonSerializer.Deserialize<ModlistManifest>(json, Options)
           ?? throw new JsonException("Manifest is null");

    public static async Task<ModlistManifest> LoadAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ModlistManifest>(fs, Options, ct)
               ?? throw new JsonException("Manifest is null");
    }

    /// <summary>
    /// Синхронная версия LoadAsync. Для verify.
    /// </summary>
    public static ModlistManifest Load(string path)
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize<ModlistManifest>(fs, Options)
               ?? throw new JsonException("Manifest is null");
    }

    public static async Task SaveAsync(string path, ModlistManifest manifest, CancellationToken ct)
    {
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, manifest, Options, ct);
    }

    /// <summary>
    /// Синхронная версия SaveAsync. Для тестов.
    /// </summary>
    public static void Save(string path, ModlistManifest manifest)
    {
        using var fs = File.Create(path);
        JsonSerializer.Serialize(fs, manifest, Options);
    }
}
