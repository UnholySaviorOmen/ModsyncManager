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
///     Name       — идентификатор (нормализованный relative path от MO2/,
///                  без trailing slash): "plugins/fomod.dll" или
///                  "tools/BethINI",
///     Directives — FromArchive-директивы с Destination ОТНОСИТЕЛЬНО
///                  корня MO2/ (не относительно mods/).
///
/// Логика для каждой entry:
///   1. Резолвим absolutePath = mo2Path / Name.
///   2. Если absolutePath — папка:
///        - собрать множество ожидаемых файлов из директив entry;
///        - проверить, что все они на диске и совпадают (прямая);
///        - проверить, что все файлы внутри папки — в множестве
///          (обратная, reconcile);
///        - если что-то не совпало → удалить папку целиком и заново;
///        - если всё совпало → Skipped.
///   3. Если absolutePath — файл:
///        - проверить hash/size; если совпал → Skipped;
///        - если нет → перезаписать.
///   4. Если ничего нет → разложить с нуля (Written).
///
/// Границы reconcile — только внутри entry. MO2/ содержит
/// дистрибутив MO2 (ModOrganizer.exe, styles/, web/, languages/,
/// dlls/, NCC/, ...). Мы не трогаем ничего вне entry из
/// config.mo2.extensions[].
///
/// При recreate удаляется папка entry (например, tools/BethINI/).
/// tools/ и соседние файлы не трогаются.
///
/// Чего НЕ делает:
///   - не reconcile-ит ничего вне границ entry;
///   - не пишет meta.ini (у extensions его нет);
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

        // Нормализуем entry.Name и резолвим absolutePath.
        var normalizedEntry = entry.Name
            .Replace('\\', '/')
            .TrimEnd('/');

        var absolutePath = Path.Combine(
            mo2Path,
            normalizedEntry.Replace('/', Path.DirectorySeparatorChar));

        // --- Определяем, что на диске ---
        if (Directory.Exists(absolutePath))
        {
            // Entry — папка. Reconcile.
            if (DirectoryMatchesManifest(entry, absolutePath, out var reason))
            {
                _logger.LogDebug(
                    "MO2 extension '{Name}': up to date",
                    entry.Name);
                return EntryAction.Skipped;
            }

            _logger.LogInformation(
                "MO2 extension '{Name}': recreating ({Reason})",
                entry.Name, reason);

            try
            {
                Directory.Delete(absolutePath, recursive: true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to delete MO2 extension folder '{absolutePath}' " +
                    $"before recreating: {ex.Message}. " +
                    $"Close Mod Organizer / game if running.", ex);
            }

            Directory.CreateDirectory(absolutePath);

            await ExtractEntryAsync(
                entry, fromArchive, mo2Path, archivesById, downloadsPath, ct);

            return EntryAction.Written;
        }

        if (File.Exists(absolutePath))
        {
            // Entry — файл. Проверяем одну директиву.
            // Ожидается ровно одна FromArchive-директива с Destination,
            // совпадающим с entry.Name.
            var directive = fromArchive.FirstOrDefault(d =>
                string.Equals(
                    d.Destination.Replace('\\', '/').TrimStart('/'),
                    normalizedEntry,
                    StringComparison.OrdinalIgnoreCase));

            if (directive is null)
            {
                // Странная ситуация: на диске файл по пути entry.Name,
                // но в директивах нет соответствующей записи.
                // Это значит, что entry.Name совпадает с другим
                // Destination — маловероятно, но возможно.
                // Считаем mismatch, перезапишем.
                _logger.LogWarning(
                    "MO2 extension '{Name}': file exists on disk but " +
                    "no matching directive — recreating",
                    entry.Name);

                try
                {
                    File.Delete(absolutePath);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to delete MO2 extension file " +
                        $"'{absolutePath}': {ex.Message}. " +
                        $"Close Mod Organizer / game if running.", ex);
                }

                await ExtractEntryAsync(
                    entry, fromArchive, mo2Path, archivesById, downloadsPath, ct);

                return EntryAction.Written;
            }

            if (FileMatches(absolutePath, directive.Hash, directive.Size))
            {
                _logger.LogDebug(
                    "MO2 extension '{Name}': up to date",
                    entry.Name);
                return EntryAction.Skipped;
            }

            _logger.LogDebug(
                "MO2 extension '{Name}': file mismatch — overwriting",
                entry.Name);

            await ExtractEntryAsync(
                entry, fromArchive, mo2Path, archivesById, downloadsPath, ct);

            return EntryAction.Written;
        }

        // Ни файла, ни папки — раскладываем с нуля.
        _logger.LogDebug(
            "MO2 extension '{Name}': creating",
            entry.Name);

        await ExtractEntryAsync(
            entry, fromArchive, mo2Path, archivesById, downloadsPath, ct);

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
                // Destination относительно MO2/. Внутри entry-папки
                // это Destination минус entry.Name + "/".
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

            // Полный relative от MO2/.
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
    /// Если префикса нет (директива Destination == entry.Name,
    /// entry — файл, но папку мы не сюда попадём) — возвращает
    /// relative как есть.
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
        string mo2Path,
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

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, overwrite: true);

                _logger.LogDebug(
                    "MO2 extension '{Name}': wrote {Dest}",
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
