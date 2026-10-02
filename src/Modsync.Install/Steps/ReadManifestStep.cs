// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Читает modlist.json и валидирует schemaVersion.
/// Не трогает целевую папку — просто читает файл.
///
/// Единственная обязательная проверка на этом шаге — schemaVersion.
/// Всё остальное (валидность ссылок между секциями, лимиты inlineFiles
/// и т.п.) уже проверено packer-ом в ValidateManifestStep. Installer
/// доверяет манифесту, но не доверяет пользователю: он мог положить
/// в папку чужой или устаревший modlist.json.
/// </summary>
public sealed class ReadManifestStep : IStep<string, ReadManifestStep.Output>
{
    private readonly ILogger<ReadManifestStep> _logger;

    public ReadManifestStep(ILogger<ReadManifestStep> logger)
    {
        _logger = logger;
    }

    public async Task<Output> ExecuteAsync(string manifestPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException(
                "Manifest path must be non-empty.", nameof(manifestPath));

        var fullPath = Path.GetFullPath(manifestPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"Manifest not found: {fullPath}", fullPath);

        _logger.LogInformation("Reading manifest: {Path}", fullPath);

        ModlistManifest manifest;
        try
        {
            manifest = await ManifestJson.LoadAsync(fullPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to parse manifest: {ex.Message}", ex);
        }

        if (!ManifestSchema.IsSupported(manifest.SchemaVersion))
        {
            var supported = string.Join(
                ", ", ManifestSchema.Supported.OrderBy(s => s));
            throw new InvalidOperationException(
                $"Unsupported schemaVersion '{manifest.SchemaVersion}'. " +
                $"This installer supports: {supported}.");
        }

        _logger.LogInformation(
            "Manifest OK: '{Name}' v{Version} ({Game}), schema {Schema}",
            manifest.Meta.Name,
            manifest.Meta.Version,
            manifest.Meta.Game,
            manifest.SchemaVersion);

        return new Output
        {
            Manifest = manifest,
            ManifestPath = fullPath,
        };
    }

    public sealed class Output
    {
        public required ModlistManifest Manifest { get; init; }
        public required string ManifestPath { get; init; }
    }
}
