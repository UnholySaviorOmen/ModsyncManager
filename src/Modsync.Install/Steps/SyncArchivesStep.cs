// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Install.Downloaders;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Обеспечивает наличие всех архивов из manifest.Archives в MO2/downloads/.
///
/// Не трогает MO2-архив — это забота BootstrapMo2Step.
///
/// Хеши считаются через IHashCache (L1 + L2). Пути к архивам
/// стабильны → L2 работает.
///
/// Прогресс: если input.DetailProgress задан, после каждого
/// завершённого архива репортится (Completed, Total). Это
/// позволяет UI показать «Downloading: 12 / 891 files completed».
///
/// Не удаляет ничего из downloads/. Не распаковывает. Не трогает моды.
/// </summary>
public sealed class SyncArchivesStep : IStep<SyncArchivesStep.Input, SyncArchivesStep.Output>
{
    private readonly DownloaderRegistry _downloaders;
    private readonly IHashCache _hashCache;
    private readonly ILogger<SyncArchivesStep> _logger;

    public SyncArchivesStep(
        DownloaderRegistry downloaders,
        IHashCache hashCache,
        ILogger<SyncArchivesStep> logger)
    {
        _downloaders = downloaders;
        _hashCache = hashCache;
        _logger = logger;
    }

    public async Task<Output> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var total = input.Manifest.Archives.Count;

        _logger.LogInformation(
            "Syncing {Count} archives into {Path}",
            total, input.DownloadsPath);

        // Стартовый репорт: 0 / N.
        input.DetailProgress?.Report((0, total));

        var localByHash = ScanDownloads(input.DownloadsPath, ct);
        _logger.LogInformation(
            "Local downloads scan: {Count} files with unique hashes",
            localByHash.Count);

        var results = new ConcurrentBag<ArchiveResult>();
        var completed = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = input.ParallelOptions.MaxDegreeOfParallelism,
            CancellationToken = ct,
        };

        await Parallel.ForEachAsync(
            input.Manifest.Archives,
            parallelOptions,
            async (archive, innerCt) =>
            {
                var result = await ProcessOneArchiveAsync(
                    archive, input.DownloadsPath, localByHash, innerCt);
                results.Add(result);

                var current = Interlocked.Increment(ref completed);
                input.DetailProgress?.Report((current, total));
            });

        // Финальный репорт — на случай, если цикл не дошёл.
        input.DetailProgress?.Report((total, total));

        var alreadyPresent = results
            .Where(r => r.Kind == ArchiveResultKind.AlreadyPresent)
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var downloaded = results
            .Where(r => r.Kind == ArchiveResultKind.Downloaded)
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skipped = results
            .Where(r => r.Kind == ArchiveResultKind.Skipped)
            .Select(r => r.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Sync complete: {Already} present, {Downloaded} downloaded, {Skipped} skipped",
            alreadyPresent.Count, downloaded.Count, skipped.Count);

        return new Output
        {
            AlreadyPresent = alreadyPresent,
            Downloaded = downloaded,
            Skipped = skipped,
        };
    }

    // ------------------------------------------------------------------
    //  Сканирование downloads/
    // ------------------------------------------------------------------

    private Dictionary<XxHash64Value, string> ScanDownloads(
        string downloadsPath, CancellationToken ct)
    {
        if (!Directory.Exists(downloadsPath))
        {
            Directory.CreateDirectory(downloadsPath);
        }

        var result = new Dictionary<XxHash64Value, string>();

        var files = Directory.EnumerateFiles(downloadsPath, "*",
            SearchOption.TopDirectoryOnly);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (file.EndsWith(ArchiveDownloadHelper.PartSuffix,
                StringComparison.OrdinalIgnoreCase))
                continue;

            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var hash = _hashCache.GetOrCompute(file);
                result.TryAdd(hash, file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to hash {File} during downloads scan — skipping",
                    file);
            }
        }

        return result;
    }

    // ------------------------------------------------------------------
    //  Обработка одного архива
    // ------------------------------------------------------------------

    private async Task<ArchiveResult> ProcessOneArchiveAsync(
        ArchiveEntry archive,
        string downloadsPath,
        Dictionary<XxHash64Value, string> localByHash,
        CancellationToken ct)
    {
        if (localByHash.TryGetValue(archive.Hash, out var existingPath))
        {
            _logger.LogDebug(
                "Archive already present: {Name} ({Hash})",
                archive.Name, archive.Hash);
            return ArchiveResult.AlreadyPresent(archive.Name);
        }

        if (archive.Sources.Count == 0)
        {
            throw new InvalidOperationException(
                $"Archive '{archive.Name}' has no sources.");
        }

        var targetPath = Path.Combine(downloadsPath, archive.Name);
        var partPath = targetPath + ArchiveDownloadHelper.PartSuffix;

        bool anySourceSkipped = false;
        var errors = new List<string>();

        foreach (var source in archive.Sources)
        {
            ct.ThrowIfCancellationRequested();

            var sourceType = ArchiveDownloadHelper.GetSourceType(source);
            var downloader = _downloaders.TryGet(sourceType);

            if (downloader is null)
            {
                _logger.LogWarning(
                    "No downloader for source type '{Type}' " +
                    "(archive '{Name}') — skipping this source",
                    sourceType, archive.Name);
                anySourceSkipped = true;
                errors.Add($"{sourceType}: no downloader registered");
                continue;
            }

            try
            {
                await ArchiveDownloadHelper.DownloadWithRetryAsync(
                    downloader,
                    source,
                    partPath,
                    archive.Hash,
                    displayName: $"archive '{archive.Name}'",
                    hashCache: _hashCache,
                    logger: _logger,
                    ct);

                File.Move(partPath, targetPath, overwrite: true);

                _logger.LogInformation(
                    "Downloaded: {Name} → {Path}",
                    archive.Name, targetPath);

                return ArchiveResult.Downloaded(archive.Name);
            }
            catch (OperationCanceledException)
            {
                ArchiveDownloadHelper.CleanupPartFile(partPath, _logger);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to download '{Name}' from source {Type}: {Message}",
                    archive.Name, sourceType, ex.Message);
                errors.Add($"{sourceType}: {ex.Message}");
                ArchiveDownloadHelper.CleanupPartFile(partPath, _logger);
            }
        }

        // Если хоть одна ошибка — «нет Nexus-ключа», даём понятное
        // сообщение. Это самая частая причина провала на чистой машине.
        var authFailure = errors.Any(e =>
            e.Contains("Nexus API key is not set", StringComparison.OrdinalIgnoreCase)
            || e.Contains("NexusAuthenticationException", StringComparison.OrdinalIgnoreCase));

        if (authFailure)
        {
            throw new InvalidOperationException(
                $"Failed to sync archive '{archive.Name}': " +
                $"Nexus API key is not set. " +
                $"Open Settings → Nexus, paste your API key, and retry. " +
                $"If you're a Free user, enable Free Download in " +
                $"Settings → Nexus Free Download.");
        }

        var reason = anySourceSkipped
            ? "some sources skipped (no downloader)"
            : "all sources failed";

        var details = errors.Count > 0
            ? " Errors: " + string.Join(" | ", errors)
            : "";

        throw new InvalidOperationException(
            $"Failed to sync archive '{archive.Name}' ({archive.Hash}): " +
            $"{reason}.{details}");
    }

    // ------------------------------------------------------------------
    //  Результаты
    // ------------------------------------------------------------------

    private enum ArchiveResultKind
    {
        AlreadyPresent,
        Downloaded,
        Skipped,
    }

    private readonly record struct ArchiveResult(string Name, ArchiveResultKind Kind)
    {
        public static ArchiveResult AlreadyPresent(string name)
            => new(name, ArchiveResultKind.AlreadyPresent);

        public static ArchiveResult Downloaded(string name)
            => new(name, ArchiveResultKind.Downloaded);

        public static ArchiveResult Skipped(string name)
            => new(name, ArchiveResultKind.Skipped);
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required ModlistManifest Manifest { get; init; }
        public required string DownloadsPath { get; init; }
        public required ParallelOptions ParallelOptions { get; init; }

        /// <summary>
        /// Опциональный репорт прогресса: (Completed, Total).
        /// Completed инкрементится после каждого завершённого архива
        /// (успех, already present, ошибка — неважно, попытка была).
        /// Если null — репорт не делается.
        /// </summary>
        public IProgress<(int Completed, int Total)>? DetailProgress { get; init; }
    }

    public sealed class Output
    {
        public required IReadOnlyList<string> AlreadyPresent { get; init; }
        public required IReadOnlyList<string> Downloaded { get; init; }
        public required IReadOnlyList<string> Skipped { get; init; }
    }
}
