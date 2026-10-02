// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Identity;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Matching;

/// <summary>
/// Построение ArchiveEntry для MO2-архива.
///
/// Логика:
///   - mo2.source обязательно MirrorSourceRef (гарантирует PackConfigValidator).
///   - Hash берётся из mo2.source.hash — источник правды.
///   - Если архив есть в downloads/ (в ArchiveIndex.Resolved или Unresolved)
///     и его hash совпадает с source.hash — берём size с диска.
///   - Если архив есть, но hash не совпадает — InvalidOperationException
///     (пользователь опечатался в конфиге или файл не тот).
///   - Если архива нет — size = 0, hash берётся из source.
///
/// Используется:
///   - BuildManifestStep — для manifest.Mo2.Archive.
///   - PackPipeline — для передачи MO2-архива в ArchiveMatcher, чтобы
///     extensions/extras могли матчиться с файлами внутри MO2-архива.
/// </summary>
internal static class Mo2ArchiveBuilder
{
    public static ArchiveEntry Build(
        PackConfig config,
        ArchiveIndex archiveIndex,
        ILogger logger)
    {
        if (config.Mo2.Source is not MirrorSourceRef mirror)
        {
            throw new InvalidOperationException(
                $"mo2.source must be a mirror source with hash. " +
                $"Got: {config.Mo2.Source.GetType().Name}. " +
                $"This should have been caught by PackConfigValidator.");
        }

        var expectedHash = mirror.Hash;

        // 1. Resolved
        var resolved = archiveIndex.Resolved
            .FirstOrDefault(a => string.Equals(
                a.Name, config.Mo2.Archive, StringComparison.OrdinalIgnoreCase));

        if (resolved is not null)
        {
            ThrowIfHashMismatch(
                config.Mo2.Archive, resolved.Hash, expectedHash);

            logger.LogDebug(
                "MO2 archive '{Name}': found in downloads/ (resolved), size={Size}",
                config.Mo2.Archive, resolved.Size);

            return new ArchiveEntry
            {
                Id = resolved.Id,
                Name = resolved.Name,
                Size = resolved.Size,
                Hash = expectedHash,
                Sources = new ArchiveSourceRef[] { config.Mo2.Source },
            };
        }

        // 2. Unresolved
        var unresolved = archiveIndex.Unresolved
            .FirstOrDefault(a => string.Equals(
                a.FileName, config.Mo2.Archive, StringComparison.OrdinalIgnoreCase));

        if (unresolved is not null)
        {
            ThrowIfHashMismatch(
                config.Mo2.Archive, unresolved.Hash, expectedHash);

            logger.LogDebug(
                "MO2 archive '{Name}': found in downloads/ (unresolved), size={Size}",
                config.Mo2.Archive, unresolved.Size);

            return new ArchiveEntry
            {
                Id = ArchiveId.FromLocal(unresolved.FileName),
                Name = unresolved.FileName,
                Size = unresolved.Size,
                Hash = expectedHash,
                Sources = new ArchiveSourceRef[] { config.Mo2.Source },
            };
        }

        // 3. Файла нет в downloads/ — hash из source, size = 0.
        logger.LogInformation(
            "MO2 archive '{Name}' not found in downloads/. " +
            "Using hash from mo2.source: {Hash} (size=0)",
            config.Mo2.Archive, expectedHash);

        return new ArchiveEntry
        {
            Id = ArchiveId.FromLocal(config.Mo2.Archive),
            Name = config.Mo2.Archive,
            Size = 0,
            Hash = expectedHash,
            Sources = new ArchiveSourceRef[] { config.Mo2.Source },
        };
    }

    private static void ThrowIfHashMismatch(
        string archiveName,
        XxHash64Value actualHash,
        XxHash64Value expectedHash)
    {
        if (actualHash == expectedHash)
            return;

        throw new InvalidOperationException(
            $"MO2 archive hash mismatch for '{archiveName}': " +
            $"file in downloads/ has {actualHash}, but mo2.source.hash is {expectedHash}. " +
            $"Update mo2.source.hash in modsyncmanager-pack.json, or replace the archive in downloads/.");
    }
}
