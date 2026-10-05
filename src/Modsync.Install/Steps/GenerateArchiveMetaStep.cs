// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Manifest;
using Modsync.Platform.MO2.Writers;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Reconcile downloads/&lt;archive&gt;.meta по манифесту.
///
/// Для каждого ArchiveEntry в manifest.Archives[]:
///   - Если entry.Meta != null — записать
///     downloads/&lt;entry.Name&gt;.meta через MetaIniWriter.
///   - Если entry.Meta == null — ничего не делать.
///     (Не удаляем .meta, если он есть. Автор мог положить его
///     вручную. Packer в следующий раз прочитает и включит в манифест.)
///
/// MO2-архив (manifest.Mo2.Archive) тоже проходит через этот шаг:
/// для него создаётся downloads/&lt;mo2-archive&gt;.meta, если Meta != null.
///
/// Не трогает mods/, не трогает Stock Game/, не трогает MO2/
/// кроме downloads/. Не скачивает архивы — это SyncArchivesStep.
/// Не пишет modlist.json — это WriteManifestStep в packer-е.
///
/// Идемпотентен: повторный запуск даёт то же состояние.
/// </summary>
public sealed class GenerateArchiveMetaStep
    : IStep<GenerateArchiveMetaStep.Input, GenerateArchiveMetaStep.Output>
{
    private const string MetaSuffix = ".meta";

    private readonly ILogger<GenerateArchiveMetaStep> _logger;

    public GenerateArchiveMetaStep(ILogger<GenerateArchiveMetaStep> logger)
    {
        _logger = logger;
    }

    public Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var downloadsPath = Path.GetFullPath(input.DownloadsPath);

        if (!Directory.Exists(downloadsPath))
        {
            throw new DirectoryNotFoundException(
                $"downloads/ not found: {downloadsPath}. " +
                $"SyncArchivesStep should have created it.");
        }

        _logger.LogInformation(
            "Generating .meta for archives in {Path}", downloadsPath);

        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var archive in input.Manifest.Archives)
        {
            ct.ThrowIfCancellationRequested();

            var meta = archive.Meta;
            if (meta is null)
            {
                skipped.Add(archive.Name);
                continue;
            }

            var metaPath = Path.Combine(downloadsPath, archive.Name + MetaSuffix);

            try
            {
                MetaIniWriter.WriteFile(metaPath, meta);
                written.Add(archive.Name);
                _logger.LogDebug(
                    "Archive '{Name}': wrote .meta", archive.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to write .meta for '{archive.Name}': " +
                    $"{ex.Message}", ex);
            }
        }

        // MO2-архив.
        ct.ThrowIfCancellationRequested();

        var mo2Archive = input.Manifest.Mo2.Archive;
        if (mo2Archive.Meta is not null)
        {
            var metaPath = Path.Combine(
                downloadsPath, mo2Archive.Name + MetaSuffix);

            try
            {
                MetaIniWriter.WriteFile(metaPath, mo2Archive.Meta);
                written.Add(mo2Archive.Name);
                _logger.LogDebug(
                    "MO2 archive '{Name}': wrote .meta", mo2Archive.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to write .meta for MO2 archive " +
                    $"'{mo2Archive.Name}': {ex.Message}", ex);
            }
        }

        _logger.LogInformation(
            "Archive .meta sync complete: {Written} written, " +
            "{Skipped} skipped (no Meta in manifest)",
            written.Count, skipped.Count);

        return Task.FromResult(new Output
        {
            Written = written,
            Skipped = skipped,
        });
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }

        /// <summary>&lt;instancePath&gt;/MO2/downloads/.</summary>
        public required string DownloadsPath { get; init; }
    }

    public sealed class Output
    {
        /// <summary>Имена архивов, для которых написан .meta.</summary>
        public required IReadOnlyList<string> Written { get; init; }

        /// <summary>
        /// Имена архивов, для которых .meta не писался
        /// (Meta == null в манифесте).
        /// </summary>
        public required IReadOnlyList<string> Skipped { get; init; }
    }
}
