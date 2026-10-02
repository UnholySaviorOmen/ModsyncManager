// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Раскладывает manifest.Mo2.Extensions[] в &lt;target&gt;/MO2/.
///
/// Что такое extensions:
///   Файлы и папки в корне MO2/, которые не являются модами
///   (mods/&lt;ModName&gt;/) и не являются самим MO2-дистрибутивом.
///   Например: MO2/plugins/fomod_plus_installer.dll, MO2/tools/BethINI/.
///   В manifest.Mo2.Extensions[] они описаны как ExtensionEntry:
///     Name       — идентификатор (для логов),
///     Directives — FromArchive-директивы с Destination ОТНОСИТЕЛЬНО
///                  корня MO2/ (не относительно mods/).
///
/// Логика:
///   1. Проверить, что &lt;target&gt;/MO2/ существует.
///   2. Для каждого entry:
///        - сгруппировать директивы по archiveId;
///        - распаковать архив во временную папку (один раз на архив);
///        - для каждой директивы: hash-match → skip, иначе copy.
///   3. Вернуть Written / Skipped.
///
/// Чего НЕ делает:
///   - не reconcile (нет modlist.txt, не с чем сверять «лишнее»);
///   - не удаляет ничего;
///   - не пишет meta.ini;
///   - не трогает mods/;
///   - не трогает Stock Game/.
///
/// Идемпотентен: повторный запуск даёт то же состояние.
/// </summary>
public sealed class ExecuteExtensionsStep
    : IStep<ExecuteExtensionsStep.Input, ExecuteExtensionsStep.Output>
{
    private const string Mo2DirName = "MO2";

    private readonly IArchiveExtractor _extractor;
    private readonly ILogger<ExecuteExtensionsStep> _logger;

    public ExecuteExtensionsStep(
        IArchiveExtractor extractor,
        ILogger<ExecuteExtensionsStep> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var mo2Path = Path.Combine(input.InstancePath, Mo2DirName);

        if (!Directory.Exists(mo2Path))
        {
            // Контракт: BootstrapInstanceStep создал MO2/ до нас.
            throw new DirectoryNotFoundException(
                $"MO2/ not found in instance: {mo2Path}. " +
                $"BootstrapInstanceStep should have created it. " +
                $"This is a pipeline contract violation.");
        }

        var entries = input.Manifest.Mo2.Extensions;

        _logger.LogInformation(
            "Executing {Count} MO2 extensions into {Path}",
            entries.Count, mo2Path);

        if (entries.Count == 0)
        {
            _logger.LogInformation(
                "No MO2 extensions in manifest — nothing to do");
            return new Output
            {
                Written = Array.Empty<string>(),
                Skipped = Array.Empty<string>(),
            };
        }

        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var action = await ProcessEntryAsync(
                entry, mo2Path, input.ArchivesById, input.DownloadsPath, ct);

            switch (action)
            {
                case EntryAction.Written:
                    written.Add(entry.Name);
                    break;
                case EntryAction.Skipped:
                    skipped.Add(entry.Name);
                    break;
            }
        }

        _logger.LogInformation(
            "MO2 extensions sync complete: {Written} written, {Skipped} skipped",
            written.Count, skipped.Count);

        return new Output
        {
            Written = written,
            Skipped = skipped,
        };
    }

    // ------------------------------------------------------------------
    //  Обработка одного entry
    // ------------------------------------------------------------------

    private async Task<EntryAction> ProcessEntryAsync(
        ExtensionEntry entry,
        string mo2Path,
        IReadOnlyDictionary<string, ArchiveEntry> archivesById,
        string downloadsPath,
        CancellationToken ct)
    {
        var fromArchive = entry.Directives
            .OfType<FromArchiveDirective>()
            .ToList();

        if (fromArchive.Count == 0)
        {
            _logger.LogDebug(
                "MO2 extension '{Name}': no FromArchive directives — skipping",
                entry.Name);
            return EntryAction.Skipped;
        }

        // Группируем по archiveId: один архив — одно распаковывание.
        var byArchive = fromArchive
            .GroupBy(d => d.Archive, StringComparer.Ordinal)
            .ToList();

        bool anyWritten = false;

        using var workspace = new TempWorkspace();

        foreach (var group in byArchive)
        {
            ct.ThrowIfCancellationRequested();

            var archiveId = group.Key;

            if (!archivesById.TryGetValue(archiveId, out var archiveEntry))
            {
                throw new InvalidOperationException(
                    $"Archive '{archiveId}' referenced by MO2 extension " +
                    $"'{entry.Name}' not found in manifest.Archives or " +
                    $"manifest.Mo2.Archive. Manifest validation should have " +
                    $"caught this.");
            }

            var archivePath = Path.Combine(downloadsPath, archiveEntry.Name);

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException(
                    $"Archive not found for MO2 extension '{entry.Name}': " +
                    $"{archivePath}. SyncArchivesStep should have " +
                    $"downloaded it.",
                    archivePath);
            }

            var extractSubdir = Path.Combine(workspace.Path, SanitizeDirName(archiveId));
            Directory.CreateDirectory(extractSubdir);

            var extractedFiles = await _extractor.ExtractAsync(
                archivePath, extractSubdir, ct);

            var extractedSet = new HashSet<string>(
                extractedFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var directive in group)
            {
                ct.ThrowIfCancellationRequested();

                if (!extractedSet.Contains(directive.Source))
                {
                    throw new InvalidOperationException(
                        $"File '{directive.Source}' not found in archive " +
                        $"'{archiveEntry.Name}' for MO2 extension " +
                        $"'{entry.Name}'. Manifest may be inconsistent " +
                        $"with archive content.");
                }

                var sourcePath = Path.Combine(
                    extractSubdir,
                    directive.Source.Replace('/', Path.DirectorySeparatorChar));

                var destPath = Path.Combine(
                    mo2Path,
                    directive.Destination.Replace('/', Path.DirectorySeparatorChar));

                if (FileMatches(destPath, directive.Hash, directive.Size))
                {
                    _logger.LogDebug(
                        "MO2 extension '{Name}': up to date ({Dest})",
                        entry.Name, directive.Destination);
                    continue;
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, overwrite: true);
                anyWritten = true;

                _logger.LogDebug(
                    "MO2 extension '{Name}': wrote {Dest}",
                    entry.Name, directive.Destination);
            }
        }

        return anyWritten ? EntryAction.Written : EntryAction.Skipped;
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private static bool FileMatches(
        string destPath,
        Core.Models.Hashing.XxHash64Value expectedHash,
        long expectedSize)
    {
        if (!File.Exists(destPath))
            return false;

        var info = new FileInfo(destPath);
        if (info.Length != expectedSize)
            return false;

        var actualHash = Core.Models.Hashing.XxHash64Value.FromFile(destPath);
        return actualHash == expectedHash;
    }

    /// <summary>
    /// archiveId может содержать символы, невалидные для имени папки
    /// (например, "nexus_skyrimspecialedition_3863_1000" — валидно,
    /// но на всякий случай подчистим).
    /// </summary>
    private static string SanitizeDirName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        /// <summary>Корень инстанса (содержит MO2/ и Stock Game/).</summary>
        public required string InstancePath { get; init; }

        /// <summary>Манифест целиком.</summary>
        public required ModlistManifest Manifest { get; init; }

        /// <summary>Папка с архивами (&lt;target&gt;/MO2/downloads/).</summary>
        public required string DownloadsPath { get; init; }

        /// <summary>
        /// Архивы по id. Включает manifest.Archives[] И manifest.Mo2.Archive.
        /// Строится в InstallPipeline.
        /// </summary>
        public required IReadOnlyDictionary<string, ArchiveEntry> ArchivesById { get; init; }
    }

    public sealed class Output
    {
        /// <summary>Имена entries, у которых были скопированы файлы.</summary>
        public required IReadOnlyList<string> Written { get; init; }

        /// <summary>Имена entries, у которых все файлы уже на месте.</summary>
        public required IReadOnlyList<string> Skipped { get; init; }
    }

    private enum EntryAction
    {
        Written,
        Skipped,
    }
}
