// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Modsync.Core.Archives;

/// <summary>
/// Общий helper для скачивания архивов с retry-логикой, .part-файлами
/// и проверкой хеша. Используется SyncArchivesStep и BootstrapMo2Step.
///
/// Логика скачивания одинакова:
///   1. Downloader отдаёт Stream.
///   2. Пишем в <target>.part.
///   3. Считаем xxHash64 .part-файла.
///   4. Сверяем с ожидаемым.
///   5. Mismatch → InvalidOperationException → retry.
///
/// File.Move(.part → target) — ответственность вызывающего кода.
/// CleanupPartFile при провале — тоже.
///
/// Хеш-кеш передаётся через IHashCache: в проде это SqliteHashCache
/// (L1 + L2), в тестах — FileHashCache. Логика helper-а не зависит
/// от реализации.
///
/// Никаких информационных логов внутри: сообщение о конкретном шаге
/// (например, «Downloaded SkyUI.7z») — дело вызывающего.
/// </summary>
public static class ArchiveDownloadHelper
{
    public const string PartSuffix = ".part";
    public const int MaxAttemptsPerSource = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static string GetSourceType(ArchiveSourceRef source) => source switch
    {
        NexusSourceRef => "nexus",
        MirrorSourceRef => "mirror",
        _ => source.GetType().Name.ToLowerInvariant(),
    };

    public static async Task DownloadWithRetryAsync(
        IArchiveDownloader downloader,
        ArchiveSourceRef source,
        string partPath,
        XxHash64Value expectedHash,
        string displayName,
        IHashCache hashCache,
        ILogger logger,
        CancellationToken ct)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxAttemptsPerSource - 1,
                Delay = RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Retry {Attempt}/{Max} for {DisplayName} from {Source}: {Error}",
                        args.AttemptNumber + 1,
                        MaxAttemptsPerSource,
                        displayName,
                        downloader.SourceType,
                        args.Outcome.Exception?.Message ?? "<no exception>");
                    return ValueTask.CompletedTask;
                },
            })
            .Build();

        await pipeline.ExecuteAsync(async token =>
        {
            await using var stream = await downloader.DownloadAsync(source, token);

            await using var fs = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None);

            await stream.CopyToAsync(fs, token);
            await fs.FlushAsync(token);
            fs.Close();

            var actual = hashCache.GetOrCompute(partPath);
            if (actual != expectedHash)
            {
                throw new InvalidOperationException(
                    $"Hash mismatch for {displayName}: " +
                    $"expected {expectedHash}, got {actual}.");
            }
        }, ct);
    }

    public static void CleanupPartFile(string partPath, ILogger logger)
    {
        try
        {
            if (File.Exists(partPath))
                File.Delete(partPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to clean up partial file: {Path}", partPath);
        }
    }
}
