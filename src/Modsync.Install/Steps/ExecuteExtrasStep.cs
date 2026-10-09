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
///
/// Формат ExtensionEntry (из BuildManifestStep):
///     Name       — нормализованный relative path от Stock Game/, без
///                  trailing slash: "skse64_loader.exe" или "enbseries".
///     Directives — FromArchive-директивы с Destination ОТНОСИТЕЛЬНО
///                  корня Stock Game/.
///
/// Логика для каждой entry:
///   1. Резолвим absolutePath = stockGamePath / Name.
///   2. Если absolutePath — папка:
///        - прямая проверка: все директивы матчатся;
///        - обратная: все файлы внутри папки упомянуты в директивах;
///        - при mismatch → удалить папку entry целиком и заново;
///        - при совпадении → Skipped.
///   3. Если absolutePath — файл:
///        - прямая проверка hash/size;
///        - при mismatch → перезаписать;
///        - при совпадении → Skipped.
///   4. Если ничего нет → создать всё (Written).
///
/// Границы reconcile — только внутри entry. Stock Game/ содержит игру
/// (SkyrimSE.exe, Data/, binkw64.dll, ...). Ничего вне entry из
/// config.stockGame.extras[] не трогается.
///
/// Чего НЕ делает:
///   - не reconcile-ит ничего вне границ entry;
///   - не пишет meta.ini (у extras его нет);
///   - не трогает mods/;
///   - не трогает MO2/.
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
            "Stock Game extras sync complete: {Written} written, " +
            "{Skipped} skipped",
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

        var normalizedEntry = entry.Name
            .Replace('\\', '/')
            .TrimEnd('/');

        var absolutePath = Path.Combine(
            stockGamePath,
            normalizedEntry.Replace('/', Path.DirectorySeparatorChar));

        // --- Определяем, что на диске ---
        if (Directory.Exists(absolutePath))
        {
            // Entry — папка. Reconcile.
            if (DirectoryMatchesManifest(entry, absolutePath, out var reason))
            {
                _logger.LogDebug(
                    "Stock Game extra '{Name}': up to date",
                    entry.Name);
                return EntryAction.Skipped;
            }

            _logger.LogInformation(
                "Stock Game extra '{Name}': recreating ({Reason})",
                entry.Name, reason);

            try
            {
                Directory.Delete(absolutePath, recursive: true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to delete Stock Game extra folder " +
                    $"'{absolutePath}' before recreating: {ex.Message}. " +
                    $"Close Mod Organizer / game if running.", ex);
            }

            Directory.CreateDirectory(absolutePath);

            await ExtractEntryAsync(
                entry, fromArchive, stockGamePath,
                archivesById, downloadsPath, ct);

            return EntryAction.Written;
        }

        if (File.Exists(absolutePath))
        {
            // Entry — файл. Ожидается одна FromArchive-директива с
            // Destination == entry.Name.
            var directive = fromArchive.FirstOrDefault(d =>
                string.Equals(
                    d.Destination.Replace('\\', '/').TrimStart('/'),
                    normalizedEntry,
                    StringComparison.OrdinalIgnoreCase));

            if (directive is null)
            {
                _logger.LogWarning(
                    "Stock Game extra '{Name}': file exists on disk but " +
                    "no matching directive — recreating",
                    entry.Name);

                try
                {
                    File.Delete(absolutePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to delete Stock Game extra file " +
                        $"'{absolutePath}': {ex.Message}. " +
                        $"Close Mod Organizer / game if running.", ex);
                }

                await ExtractEntryAsync(
                    entry, fromArchive, stockGamePath,
                    archivesById, downloadsPath, ct);

                return EntryAction.Written;
            }

            if (FileMatches(absolutePath, directive.Hash, directive.Size))
            {
                _logger.LogDebug(
                    "Stock Game extra '{Name}': up to date",
                    entry.Name);
                return EntryAction.Skipped;
            }

            _logger.LogDebug(
                "Stock Game extra '{Name}': file mismatch — overwriting",
                entry.Name);

            await ExtractEntryAsync(
                entry, fromArchive, stockGamePath,
                archivesById, downloadsPath, ct);

            return EntryAction.Written;
        }

        // Ни файла, ни папки — раскладываем с нуля.
        _logger.LogDebug(
            "Stock Game extra '{Name}': creating",
            entry.Name);

        await ExtractEntryAsync(
            entry, fromArchive, stockGamePath,
            archivesById, downloadsPath, ct);

        return EntryAction.Written;
    }

    // ------------------------------------------------------------------
    //  Reconcile: сравнение папки с манифестом
    // ------------------------------------------------------------------

    /// <summary>
    /// true, если содержимое папки entry полностью соответствует
    /// директивам entry: все директивы матчатся (прямая), и на диске
    /// нет файлов, которых нет в директивах (обратная).
    /// </summary>
    private static bool DirectoryMatchesManifest(
        ExtensionEntry entry, string absolutePath, out string mismatchReason)
    {
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directive in entry.Directives)
        {
            if (directive is not FromArchiveDirective fromArchive)
            {
                mismatchReason =
                    $"unsupported directive type {directive.GetType().Name}";
                return false;
            }

            var relative = fromArchive.Destination
                .Replace('\\', '/')
                .TrimStart('/');

            expectedFiles.Add(relative);

            var destPath = Path.Combine(
                absolutePath,
                TrimEntryPrefix(relative, entry.Name));

            if (!File.Exists(destPath))
            {
                mismatchReason = $"missing {fromArchive.Destination}";
                return false;
            }

            var info = new FileInfo(destPath);
            if (info.Length != fromArchive.Size)
            {
                mismatchReason =
                    $"size mismatch on {fromArchive.Destination} " +
                    $"(expected {fromArchive.Size}, got {info.Length})";
                return false;
            }

            var actualHash = Core.Models.Hashing.XxHash64Value.FromFile(destPath);
            if (actualHash != fromArchive.Hash)
            {
                mismatchReason = $"hash mismatch on {fromArchive.Destination}";
                return false;
            }
        }

        // Обратная проверка: все файлы внутри папки — в expectedFiles.
        foreach (var diskFile in Directory.EnumerateFiles(
            absolutePath, "*", SearchOption.AllDirectories))
        {
            var relativeInsideEntry = Path
                .GetRelativePath(absolutePath, diskFile)
                .Replace('\\', '/');

            var normalizedEntry = entry.Name
                .Replace('\\', '/')
                .TrimEnd('/');

            var fullRelative = normalizedEntry + "/" + relativeInsideEntry;

            if (!expectedFiles.Contains(fullRelative))
            {
                mismatchReason =
                    $"unexpected file on disk: {fullRelative}";
                return false;
            }
        }

        mismatchReason = "";
        return true;
    }

    /// <summary>
    /// Обрезает у relative-пути префикс entry.Name + "/".
    /// </summary>
    private static string TrimEntryPrefix(string relative, string entryName)
    {
        var normalizedEntry = entryName
            .Replace('\\', '/')
            .TrimEnd('/');

        var prefix = normalizedEntry + "/";
        if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return relative[prefix.Length..];

        return relative;
    }

    // ------------------------------------------------------------------
    //  Раскладка файлов entry
    // ------------------------------------------------------------------

    private async Task ExtractEntryAsync(
        ExtensionEntry entry,
        IReadOnlyList<FromArchiveDirective> fromArchive,
        string stockGamePath,
        IReadOnlyDictionary<string, ArchiveEntry> archivesById,
        string downloadsPath,
        CancellationToken ct)
    {
        var byArchive = fromArchive
            .GroupBy(d => d.Archive, StringComparer.Ordinal)
            .ToList();

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

            var extractSubdir = Path.Combine(
                workspace.Path, SanitizeDirName(archiveId));
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

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, overwrite: true);

                _logger.LogDebug(
                    "Stock Game extra '{Name}': wrote {Dest}",
                    entry.Name, directive.Destination);
            }
        }
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
