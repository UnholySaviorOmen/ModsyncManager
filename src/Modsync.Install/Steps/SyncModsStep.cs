// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Reconcile mods/ под манифест.
///
/// Два прохода:
///   1. Раскладка: для каждого мода из manifest.Mods.
///   2. Удаление: пройти по mods/, удалить папки, которых нет в манифесте.
///
/// Поддерживаются только FromArchive-директивы.
///
/// Логика reconcile мода (Pass 1):
///   - Папки нет → создать, разложить.
///   - Папка есть → проверить ModMatchesManifest:
///       * все директивы матчатся (файл есть, размер/hash совпал);
///       * все файлы на диске в mods/&lt;Name&gt;/ упомянуты в директивной
///         мапе по Destination (обратная проверка).
///     Если что-то не совпало → recreate (Directory.Delete + заново).
///     Если всё совпало → skip.
///
/// Обратная проверка ловит два сценария, которые раньше приводили
/// к «файлам-призракам»:
///   - файл удалён автором из манифеста, но остался на диске
///     с прошлой версии сборки;
///   - файл переехал из одного мода в другой: старый мод skip-ается,
///     новый создаётся, файл дублируется.
///
/// Исключение из обратной проверки: корневой meta.ini
/// (mods/&lt;Name&gt;/meta.ini). Он не в директивах — его пишет
/// GenerateMetaIniStep, который идёт после SyncModsStep.
/// meta.ini в подпапках (например, fomod/meta.ini) — обычный файл,
/// должен быть в директивах.
///
/// Хеши файлов берутся через IHashCache (L1 + L2). Пути к файлам
/// модов стабильны → L2 работает. При неизменённых файлах второй
/// прогон install не читает их с диска.
///
/// Прогресс: input.DetailProgress репортит (Completed, Total) для
/// Pass 1 (раскладка). Pass 2 (удаление) — быстрый, без прогресса.
///
/// [NoDelete] и #... — регистронезависимые проверки.
/// </summary>
public sealed class SyncModsStep : IStep<SyncModsStep.Input, SyncModsStep.Output>
{
    private const string NoDeleteMarker = "[NoDelete]";
    private const char SeparatorPrefix = '#';
    private const int ProgressLogEvery = 50;

    /// <summary>
    /// Имя файла meta.ini в корне папки мода. Исключается из
    /// обратной проверки: его пишет GenerateMetaIniStep.
    /// </summary>
    private const string MetaIniFileName = "meta.ini";

    private readonly IArchiveExtractor _extractor;
    private readonly IHashCache _hashCache;
    private readonly ILogger<SyncModsStep> _logger;

    public SyncModsStep(
        IArchiveExtractor extractor,
        IHashCache hashCache,
        ILogger<SyncModsStep> logger)
    {
        _extractor = extractor;
        _hashCache = hashCache;
        _logger = logger;
    }

    public async Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var downloadsPath = Path.GetFullPath(input.DownloadsPath);
        var modsPath = Path.GetFullPath(input.ModsPath);

        _logger.LogInformation(
            "Syncing {Count} mods into {ModsPath}",
            input.Manifest.Mods.Count, modsPath);

        Directory.CreateDirectory(modsPath);

        var pass1 = await DoPass1Async(input, downloadsPath, modsPath, ct);
        var deleted = await DoPass2Async(input, modsPath, ct);

        _logger.LogInformation(
            "Sync complete: {Created} created, {Recreated} recreated, " +
            "{Skipped} skipped, {Deleted} deleted",
            pass1.Created.Count, pass1.Recreated.Count,
            pass1.Skipped.Count, deleted.Count);

        return new Output
        {
            Created = pass1.Created,
            Recreated = pass1.Recreated,
            Skipped = pass1.Skipped,
            Deleted = deleted,
        };
    }

    // ------------------------------------------------------------------
    //  Проход 1: раскладка
    // ------------------------------------------------------------------

    private async Task<Pass1Result> DoPass1Async(
        Input input, string downloadsPath, string modsPath, CancellationToken ct)
    {
        var created = new ConcurrentBag<string>();
        var recreated = new ConcurrentBag<string>();
        var skipped = new ConcurrentBag<string>();

        var total = input.Manifest.Mods.Count;

        // Стартовый репорт: 0 / N.
        input.DetailProgress?.Report((0, total));

        int processed = 0;
        int completed = 0;
        var progressLock = new object();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = input.ParallelOptions.MaxDegreeOfParallelism,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(
            input.Manifest.Mods,
            parallelOptions,
            async (mod, innerCt) =>
            {
                var action = await ProcessOneModAsync(
                    mod, input.ArchivesById, downloadsPath, modsPath, innerCt);

                switch (action)
                {
                    case ModAction.Created:
                        created.Add(mod.Name);
                        break;
                    case ModAction.Recreated:
                        recreated.Add(mod.Name);
                        break;
                    case ModAction.Skipped:
                        skipped.Add(mod.Name);
                        break;
                }

                var current = Interlocked.Increment(ref completed);
                input.DetailProgress?.Report((current, total));

                lock (progressLock)
                {
                    processed++;
                    if (processed % ProgressLogEvery == 0 || processed == total)
                    {
                        _logger.LogInformation(
                            "Sync progress: {Processed}/{Total} mods",
                            processed, total);
                    }
                }
            });

        // Финальный репорт — на случай, если цикл не дошёл.
        input.DetailProgress?.Report((total, total));

        return new Pass1Result(
            created.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            recreated.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            skipped.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private async Task<ModAction> ProcessOneModAsync(
        ModEntry mod,
        IReadOnlyDictionary<string, ArchiveEntry> archivesById,
        string downloadsPath,
        string modsPath,
        CancellationToken ct)
    {
        if (mod.Name.Contains(NoDeleteMarker, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Mod '{Name}' contains [NoDelete] — skipping", mod.Name);
            return ModAction.Skipped;
        }

        if (IsSeparator(mod.Name))
        {
            _logger.LogDebug(
                "Mod '{Name}' is a separator (#...) — skipping", mod.Name);
            return ModAction.Skipped;
        }

        var modDir = Path.Combine(modsPath, mod.Name);

        if (!Directory.Exists(modDir))
        {
            _logger.LogDebug("Mod '{Name}': creating", mod.Name);
            Directory.CreateDirectory(modDir);
            await ExtractAllDirectivesAsync(
                mod, modDir, archivesById, downloadsPath, ct);
            return ModAction.Created;
        }

        if (ModMatchesManifest(mod, modDir, out var mismatchReason))
        {
            _logger.LogDebug("Mod '{Name}': up to date", mod.Name);
            return ModAction.Skipped;
        }

        _logger.LogInformation(
            "Mod '{Name}': recreating ({Reason})", mod.Name, mismatchReason);

        try
        {
            Directory.Delete(modDir, recursive: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to delete mod folder '{modDir}' " +
                $"before recreating: {ex.Message}. " +
                $"Close Mod Organizer / game if running.", ex);
        }

        Directory.CreateDirectory(modDir);
        await ExtractAllDirectivesAsync(
            mod, modDir, archivesById, downloadsPath, ct);
        return ModAction.Recreated;
    }

    // ------------------------------------------------------------------
    //  Проверка соответствия мода манифесту
    // ------------------------------------------------------------------

    /// <summary>
    /// true, если содержимое mods/&lt;Name&gt;/ полностью соответствует
    /// манифесту: все директивы матчатся, и на диске нет файлов,
    /// которых нет в директивах.
    ///
    /// Исключение из обратной проверки: корневой meta.ini.
    /// </summary>
    private bool ModMatchesManifest(
        ModEntry mod, string modDir, out string mismatchReason)
    {
        // --- Прямая проверка: все директивы матчатся. ---
        // Строим множество ожидаемых Destination'ов (относительных
        // путей с прямыми слэшами) — пригодится для обратной проверки.
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directive in mod.Directives)
        {
            if (directive is not FromArchiveDirective fromArchive)
            {
                mismatchReason =
                    $"unsupported directive type {directive.GetType().Name}";
                return false;
            }

            var destination = fromArchive.Destination.Replace('\\', '/');

            expectedFiles.Add(destination);

            var destPath = Path.Combine(
                modDir,
                fromArchive.Destination.Replace('/', Path.DirectorySeparatorChar));

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

            var actualHash = _hashCache.GetOrCompute(destPath);
            if (actualHash != fromArchive.Hash)
            {
                mismatchReason = $"hash mismatch on {fromArchive.Destination}";
                return false;
            }
        }

        // --- Обратная проверка: на диске нет лишних файлов. ---
        // Обходим все файлы в mods/<Name>/ (рекурсивно). Каждый
        // относительный путь должен быть в expectedFiles.
        // Корневой meta.ini — исключение.
        foreach (var diskFile in Directory.EnumerateFiles(
            modDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path
                .GetRelativePath(modDir, diskFile)
                .Replace('\\', '/');

            // Исключение: корневой meta.ini.
            if (string.Equals(
                    relative, MetaIniFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expectedFiles.Contains(relative))
            {
                mismatchReason = $"unexpected file on disk: {relative}";
                return false;
            }
        }

        mismatchReason = "";
        return true;
    }

    private async Task ExtractAllDirectivesAsync(
        ModEntry mod,
        string modDir,
        IReadOnlyDictionary<string, ArchiveEntry> archivesById,
        string downloadsPath,
        CancellationToken ct)
    {
        var byArchive = mod.Directives
            .OfType<FromArchiveDirective>()
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
                    $"Archive '{archiveId}' referenced by mod '{mod.Name}' " +
                    $"not found in manifest.Archives. " +
                    $"Manifest validation should have caught this.");
            }

            var archivePath = Path.Combine(downloadsPath, archiveEntry.Name);

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException(
                    $"Archive not found for mod '{mod.Name}': {archivePath}. " +
                    $"SyncArchivesStep should have downloaded it.",
                    archivePath);
            }

            var extractSubdir = Path.Combine(workspace.Path, archiveId);
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
                        $"'{archiveEntry.Name}' for mod '{mod.Name}'. " +
                        $"Manifest may be inconsistent with archive content.");
                }

                var sourcePath = Path.Combine(
                    extractSubdir,
                    directive.Source.Replace('/', Path.DirectorySeparatorChar));

                var destPath = Path.Combine(
                    modDir,
                    directive.Destination.Replace('/', Path.DirectorySeparatorChar));

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, overwrite: true);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Проход 2: удаление лишних папок
    // ------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> DoPass2Async(
        Input input, string modsPath, CancellationToken ct)
    {
        if (!Directory.Exists(modsPath))
            return Array.Empty<string>();

        var declaredNames = new HashSet<string>(
            input.Manifest.Mods.Select(m => m.Name),
            StringComparer.OrdinalIgnoreCase);

        var toDelete = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(modsPath))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(dir);

            if (name.Contains(NoDeleteMarker, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsSeparator(name))
                continue;

            if (declaredNames.Contains(name))
                continue;

            toDelete.Add(dir);
        }

        if (toDelete.Count == 0)
            return Array.Empty<string>();

        var deleted = new ConcurrentBag<string>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = input.ParallelOptions.MaxDegreeOfParallelism,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(
            toDelete,
            parallelOptions,
            (dir, _) =>
            {
                var name = Path.GetFileName(dir);
                _logger.LogInformation(
                    "Deleting undeclared mod folder: {Name}", name);

                try
                {
                    Directory.Delete(dir, recursive: true);
                    deleted.Add(name);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to delete mod folder '{dir}': {ex.Message}. " +
                        $"Close Mod Organizer / game if running.", ex);
                }

                return ValueTask.CompletedTask;
            });

        return deleted
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSeparator(string name)
        => name.Length > 0 && name[0] == SeparatorPrefix;

    private enum ModAction
    {
        Created,
        Recreated,
        Skipped,
    }

    private readonly record struct Pass1Result(
        IReadOnlyList<string> Created,
        IReadOnlyList<string> Recreated,
        IReadOnlyList<string> Skipped);

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
        public required string DownloadsPath { get; init; }
        public required string ModsPath { get; init; }
        public required ParallelOptions ParallelOptions { get; init; }
        public required IReadOnlyDictionary<string, ArchiveEntry> ArchivesById { get; init; }

        /// <summary>
        /// Опциональный репорт прогресса Pass 1: (Completed, Total).
        /// Completed инкрементится после каждого мода (create/recreate/skip).
        /// Pass 2 (удаление) не репортит — он быстрый.
        /// Если null — репорт не делается.
        /// </summary>
        public IProgress<(int Completed, int Total)>? DetailProgress { get; init; }
    }

    public sealed class Output
    {
        public required IReadOnlyList<string> Created { get; init; }
        public required IReadOnlyList<string> Recreated { get; init; }
        public required IReadOnlyList<string> Skipped { get; init; }
        public required IReadOnlyList<string> Deleted { get; init; }
    }
}
