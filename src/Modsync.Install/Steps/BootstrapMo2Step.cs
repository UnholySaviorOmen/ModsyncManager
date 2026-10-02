// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Install.Downloaders;
using Microsoft.Extensions.Logging;

namespace Modsync.Install.Steps;

/// <summary>
/// Восстанавливает структуру MO2 в &lt;instancePath&gt;/MO2/.
///
/// Самодостаточен: сам скачивает MO2-архив (если его нет локально),
/// сам распаковывает.
///
/// Хеши считаются через IHashCache (L1 + L2). Путь MO2-архива
/// стабилен между прогонами → L2 работает.
/// </summary>
public sealed class BootstrapMo2Step : IStep<BootstrapMo2Step.Input, BootstrapMo2Step.Input>
{
    private const string Mo2DirName = "MO2";
    private const string DownloadsDirName = "downloads";

    private readonly DownloaderRegistry _downloaders;
    private readonly IHashCache _hashCache;
    private readonly IArchiveExtractor _extractor;
    private readonly ILogger<BootstrapMo2Step> _logger;

    public BootstrapMo2Step(
        DownloaderRegistry downloaders,
        IHashCache hashCache,
        IArchiveExtractor extractor,
        ILogger<BootstrapMo2Step> logger)
    {
        _downloaders = downloaders;
        _hashCache = hashCache;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<Input> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var mo2Path = Path.Combine(input.InstancePath, Mo2DirName);
        var downloadsPath = Path.Combine(mo2Path, DownloadsDirName);
        Directory.CreateDirectory(mo2Path);
        Directory.CreateDirectory(downloadsPath);

        var archive = input.Manifest.Mo2.Archive;
        var archivePath = Path.Combine(downloadsPath, archive.Name);

        _logger.LogInformation(
            "Bootstrapping MO2: {Version} from {Archive}",
            input.Manifest.Mo2.Version, archive.Name);

        var localHash = TryGetLocalHash(archivePath);

        if (localHash.HasValue && localHash.Value == archive.Hash)
        {
            _logger.LogInformation(
                "MO2 archive already present: {Path}", archivePath);
        }
        else
        {
            await DownloadMo2ArchiveAsync(archive, archivePath, ct);
        }

        _logger.LogInformation(
            "Extracting MO2 into {Mo2Path}", mo2Path);

        var extracted = await _extractor.ExtractAsync(archivePath, mo2Path, ct);

        _logger.LogInformation(
            "MO2 bootstrapped: {Count} files extracted", extracted.Count);

        return input;
    }

    private XxHash64Value? TryGetLocalHash(string archivePath)
    {
        if (!File.Exists(archivePath))
            return null;

        try
        {
            return _hashCache.GetOrCompute(archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to hash existing MO2 archive {Path} — will redownload",
                archivePath);
            return null;
        }
    }

    private async Task DownloadMo2ArchiveAsync(
        ArchiveEntry archive,
        string archivePath,
        CancellationToken ct)
    {
        if (archive.Sources.Count == 0)
        {
            throw new InvalidOperationException(
                $"MO2 archive '{archive.Name}' has no sources.");
        }

        var partPath = archivePath + ArchiveDownloadHelper.PartSuffix;

        bool anySourceSkipped = false;

        foreach (var source in archive.Sources)
        {
            ct.ThrowIfCancellationRequested();

            var sourceType = ArchiveDownloadHelper.GetSourceType(source);
            var downloader = _downloaders.TryGet(sourceType);

            if (downloader is null)
            {
                _logger.LogWarning(
                    "No downloader for source type '{Type}' (MO2 archive) — skipping",
                    sourceType);
                anySourceSkipped = true;
                continue;
            }

            try
            {
                await ArchiveDownloadHelper.DownloadWithRetryAsync(
                    downloader,
                    source,
                    partPath,
                    archive.Hash,
                    displayName: "MO2 archive",
                    hashCache: _hashCache,
                    logger: _logger,
                    ct);

                File.Move(partPath, archivePath, overwrite: true);

                _logger.LogInformation(
                    "MO2 archive downloaded: {Path}", archivePath);

                return;
            }
            catch (OperationCanceledException)
            {
                ArchiveDownloadHelper.CleanupPartFile(partPath, _logger);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to download MO2 from {Type}: {Message}",
                    sourceType, ex.Message);
                ArchiveDownloadHelper.CleanupPartFile(partPath, _logger);
            }
        }

        var reason = anySourceSkipped
            ? "some sources skipped (nexus not yet supported)"
            : "all sources failed";

        throw new InvalidOperationException(
            $"Failed to download MO2 archive '{archive.Name}' ({archive.Hash}): {reason}.");
    }

    public sealed class Input
    {
        public required string InstancePath { get; init; }
        public required ModlistManifest Manifest { get; init; }
    }
}
