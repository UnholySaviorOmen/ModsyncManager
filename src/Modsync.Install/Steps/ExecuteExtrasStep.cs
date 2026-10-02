// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Раскладывает manifest.StockGame.Extras[] в &lt;target&gt;/Stock Game/.
///
/// Что такое extras:
///   Файлы и папки в корне Stock Game/, которые не являются модами
///   (SKSE, ENB, CommunityShaders, патченный SkyrimSE.exe и т.п.).
///   В manifest.StockGame.Extras[] они описаны как ExtensionEntry:
///     Name       — идентификатор (для логов),
///     Directives — FromArchive-директивы с Destination ОТНОСИТЕЛЬНО
///                  корня Stock Game/.
///
/// Логика:
///   1. Проверить, что &lt;target&gt;/Stock Game/ существует.
///   2. Для каждого entry:
///        - сгруппировать директивы по archiveId;
///        - распаковать архив во временную папку (один раз на архив);
///        - для каждой директивы: hash-match → skip, иначе copy.
///   3. Вернуть Written / Skipped.
///
/// Чего НЕ делает:
///   - не reconcile (файлы от старой версии сборки остаются);
///   - не удаляет ничего;
///   - не пишет meta.ini;
///   - не трогает mods/;
///   - не трогает MO2/.
///
/// ⚠ Ограничение: reconcile Stock Game/ не поддерживается в MVP.
///    См. DOC.md, раздел «Пайплайн: обновление сборки».
///
/// Идемпотентен: повторный запуск даёт то же состояние.
/// </summary>
public sealed class ExecuteExtrasStep
    : IStep<ExecuteExtrasStep.Input, ExecuteExtrasStep.Output>
{
    private const string StockGameDirName = "Stock Game";

    private readonly IArchiveExtractor _extractor;
    private readonly ILogger<ExecuteExtrasStep> _logger;

    public ExecuteExtrasStep(
        IArchiveExtractor extractor,
        ILogger<ExecuteExtrasStep> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var stockGamePath = Path.Combine(input.InstancePath, StockGameDirName);

        if (!Directory.Exists(stockGamePath))
        {
            throw new DirectoryNotFoundException(
                $"Stock Game/ not found in instance: {stockGamePath}. " +
                $"BootstrapInstanceStep should have created it. " +
                $"This is a pipeline contract violation.");
        }

        var entries = input.Manifest.StockGame.Extras;

        _logger.LogInformation(
            "Executing {Count} Stock Game extras into {Path}",
            entries.Count, stockGamePath);

        if (entries.Count == 0)
        {
            _logger.LogInformation(
                "No Stock Game extras in manifest — nothing to do");
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
                entry, stockGamePath, input.ArchivesById, input.DownloadsPath, ct);

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
            "Stock Game extras sync complete: {Written} written, {Skipped} skipped",
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
        string stockGamePath,
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
                "Stock Game extra '{Name}': no FromArchive directives — skipping",
                entry.Name);
            return EntryAction.Skipped;
        }

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
                    $"Archive '{archiveId}' referenced by Stock Game extra " +
                    $"'{entry.Name}' not found in manifest.Archives or " +
                    $"manifest.Mo2.Archive. Manifest validation should have " +
                    $"caught this.");
            }

            var archivePath = Path.Combine(downloadsPath, archiveEntry.Name);

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException(
                    $"Archive not found for Stock Game extra '{entry.Name}': " +
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
                        $"'{archiveEntry.Name}' for Stock Game extra " +
                        $"'{entry.Name}'. Manifest may be inconsistent " +
                        $"with archive content.");
                }

                var sourcePath = Path.Combine(
                    extractSubdir,
                    directive.Source.Replace('/', Path.DirectorySeparatorChar));

                var destPath = Path.Combine(
                    stockGamePath,
                    directive.Destination.Replace('/', Path.DirectorySeparatorChar));

                if (FileMatches(destPath, directive.Hash, directive.Size))
                {
                    _logger.LogDebug(
                        "Stock Game extra '{Name}': up to date ({Dest})",
                        entry.Name, directive.Destination);
                    continue;
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, overwrite: true);
                anyWritten = true;

                _logger.LogDebug(
                    "Stock Game extra '{Name}': wrote {Dest}",
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
        public required string InstancePath { get; init; }
        public required ModlistManifest Manifest { get; init; }
        public required string DownloadsPath { get; init; }
        public required IReadOnlyDictionary<string, ArchiveEntry> ArchivesById { get; init; }
    }

    public sealed class Output
    {
        public required IReadOnlyList<string> Written { get; init; }
        public required IReadOnlyList<string> Skipped { get; init; }
    }

    private enum EntryAction
    {
        Written,
        Skipped,
    }
}
