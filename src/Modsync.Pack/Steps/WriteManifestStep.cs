// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Записывает ModlistManifest в modlist.json.
///
/// Место записи — __ModsyncManager_Output/modlist.json (рядом с папкой mods/,
/// куда MatchStep выгружает unmatched-файлы).
///
/// Возвращает полный путь к записанному файлу.
///
/// Если __ModsyncManager_Output не существует — ОШИБКА. Это означает, что
/// MatchStep не выполнялся или pipeline собрана неправильно.
///
/// Если modlist.json уже существует — перезаписываем с предупреждением.
/// </summary>
public sealed class WriteManifestStep : IStep<WriteManifestStep.Input, string>
{
    private const string ManifestFileName = "modlist.json";

    private readonly ILogger<WriteManifestStep> _logger;

    public WriteManifestStep(ILogger<WriteManifestStep> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(Input input, CancellationToken ct)
    {
        if (!Directory.Exists(input.ModsyncOutputPath))
        {
            throw new DirectoryNotFoundException(
                $"__ModsyncManager_Output not found: {input.ModsyncOutputPath}. " +
                $"MatchStep must run before WriteManifestStep.");
        }

        var manifestPath = Path.Combine(input.ModsyncOutputPath, ManifestFileName);

        if (File.Exists(manifestPath))
        {
            _logger.LogWarning(
                "Manifest already exists, overwriting: {Path}", manifestPath);
        }

        _logger.LogInformation("Writing manifest to {Path}", manifestPath);

        try
        {
            await ManifestJson.SaveAsync(manifestPath, input.Manifest, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to write manifest to '{manifestPath}': {ex.Message}", ex);
        }

        var size = new FileInfo(manifestPath).Length;
        _logger.LogInformation(
            "Manifest written: {Size} bytes, {Mods} mods, {Archives} archives",
            size,
            input.Manifest.Mods.Count,
            input.Manifest.Archives.Count + 1);   // +1 = mo2.archive

        return manifestPath;
    }

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
        public required string ModsyncOutputPath { get; init; }
    }
}
