// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Pack;
using Modsync.Core.Progress;
using Modsync.Pack.Matching;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack;

public sealed class PackPipeline
{
    private readonly ReadConfigStep _readConfig;
    private readonly ReadInstanceStep _readInstance;
    private readonly IndexArchivesStep _indexArchives;
    private readonly ScanModsStep _scanMods;
    private readonly ScanExtensionsStep _scanExtensions;
    private readonly ScanExtrasStep _scanExtras;
    private readonly MatchStep _match;
    private readonly MatchExtensionsStep _matchExtensions;
    private readonly MatchExtrasStep _matchExtras;
    private readonly BuildManifestStep _buildManifest;
    private readonly ValidateManifestStep _validateManifest;
    private readonly WriteManifestStep _writeManifest;

    private readonly IArchiveExtractor _extractor;

    /// <summary>
    /// FileHashCache (in-memory) — только для ArchiveMatcher.
    /// Persist не используется: ArchiveMatcher работает с temp-папками,
    /// пути меняются каждый прогон.
    /// </summary>
    private readonly FileHashCache _fileHashCache;

    private readonly ILogger<PackPipeline> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private static readonly string[] StepNames =
    {
        "ReadConfig",
        "ReadInstance",
        "IndexArchives",
        "ScanMods",
        "ScanExtensions",
        "ScanExtras",
        "BuildArchiveMatcher",
        "Match",
        "MatchExtensions",
        "MatchExtras",
        "WriteUnmatchedExtensionsExtras",
        "BuildManifest",
        "ValidateManifest",
        "WriteManifest",
    };

    public PackPipeline(
        ReadConfigStep readConfig,
        ReadInstanceStep readInstance,
        IndexArchivesStep indexArchives,
        ScanModsStep scanMods,
        ScanExtensionsStep scanExtensions,
        ScanExtrasStep scanExtras,
        MatchStep match,
        MatchExtensionsStep matchExtensions,
        MatchExtrasStep matchExtras,
        BuildManifestStep buildManifest,
        ValidateManifestStep validateManifest,
        WriteManifestStep writeManifest,
        IArchiveExtractor extractor,
        FileHashCache fileHashCache,
        ILoggerFactory loggerFactory,
        ILogger<PackPipeline> logger)
    {
        _readConfig = readConfig;
        _readInstance = readInstance;
        _indexArchives = indexArchives;
        _scanMods = scanMods;
        _scanExtensions = scanExtensions;
        _scanExtras = scanExtras;
        _match = match;
        _matchExtensions = matchExtensions;
        _matchExtras = matchExtras;
        _buildManifest = buildManifest;
        _validateManifest = validateManifest;
        _writeManifest = writeManifest;
        _extractor = extractor;
        _fileHashCache = fileHashCache;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<PackResult> ExecuteAsync(
        Input input,
        CancellationToken ct,
        IProgress<StepProgress>? progress = null)
    {
        _logger.LogInformation("=== Modsync Manager pack started ===");
        _logger.LogInformation("Config: {Path}", input.ConfigPath);

        int totalSteps = StepNames.Length;

        progress?.Report(new StepProgress(1, totalSteps, StepNames[0]));
        var config = await _readConfig.ExecuteAsync(input.ConfigPath, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(2, totalSteps, StepNames[1]));
        var snapshot = await _readInstance.ExecuteAsync(
            new ReadInstanceStep.Input
            {
                ConfigPath = input.ConfigPath,
                Config = config,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(3, totalSteps, StepNames[2]));
        var archiveIndex = await _indexArchives.ExecuteAsync(
            new IndexArchivesStep.Input
            {
                DownloadsPath = snapshot.DownloadsPath,
                Config = config,
                GameDomain = config.Meta.Game,
                ParallelOptions = input.ParallelOptions,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(4, totalSteps, StepNames[3]));
        var modScan = await _scanMods.ExecuteAsync(
            new ScanModsStep.Input
            {
                Snapshot = snapshot,
                ParallelOptions = input.ParallelOptions,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(5, totalSteps, StepNames[4]));
        var extensionsScan = await _scanExtensions.ExecuteAsync(
            new ScanExtensionsStep.Input
            {
                Config = config,
                Snapshot = snapshot,
                ParallelOptions = input.ParallelOptions,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(6, totalSteps, StepNames[5]));
        var extrasScan = await _scanExtras.ExecuteAsync(
            new ScanExtrasStep.Input
            {
                Config = config,
                Snapshot = snapshot,
                ParallelOptions = input.ParallelOptions,
            }, ct);
        ct.ThrowIfCancellationRequested();

        var archiveIndexWithMo2 = AddMo2ArchiveToResolved(config, archiveIndex);

        var matcher = new ArchiveMatcher(
            archiveIndexWithMo2,
            snapshot.DownloadsPath,
            _extractor,
            _fileHashCache,             // ← in-memory, без persist
            _loggerFactory.CreateLogger<ArchiveMatcher>());

        progress?.Report(new StepProgress(7, totalSteps, StepNames[6]));
        await matcher.BuildAsync(ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(8, totalSteps, StepNames[7]));
        var match = await _match.ExecuteAsync(
            new MatchStep.Input
            {
                ArchiveIndex = archiveIndex,
                ModScan = modScan,
                DownloadsPath = snapshot.DownloadsPath,
                ModsPath = snapshot.ModsPath,
                ModsyncOutputPath = snapshot.ModsyncOutputPath,
                Matcher = matcher,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(9, totalSteps, StepNames[8]));
        var extensionsMatch = await _matchExtensions.ExecuteAsync(
            new MatchExtensionsStep.Input
            {
                Scan = extensionsScan,
                Matcher = matcher,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(10, totalSteps, StepNames[9]));
        var extrasMatch = await _matchExtras.ExecuteAsync(
            new MatchExtrasStep.Input
            {
                Scan = extrasScan,
                Matcher = matcher,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(11, totalSteps, StepNames[10]));

        WriteUnmatchedEntries(
            entries: extensionsMatch.Unmatched,
            sourceRoot: snapshot.Mo2Path,
            outputRoot: Path.Combine(snapshot.ModsyncOutputPath, "MO2"),
            logContext: "extensions",
            keepNames: new[] { "mods" });

        WriteUnmatchedEntries(
            entries: extrasMatch.Unmatched,
            sourceRoot: snapshot.StockGamePath,
            outputRoot: Path.Combine(snapshot.ModsyncOutputPath, "Stock Game"),
            logContext: "extras",
            keepNames: Array.Empty<string>());

        progress?.Report(new StepProgress(12, totalSteps, StepNames[11]));
        var manifest = await _buildManifest.ExecuteAsync(
            new BuildManifestStep.Input
            {
                Config = config,
                Snapshot = snapshot,
                ArchiveIndex = archiveIndex,
                ModScan = modScan,
                Match = match,
                ExtensionsScan = extensionsScan,
                ExtrasScan = extrasScan,
                ExtensionsMatch = extensionsMatch,
                ExtrasMatch = extrasMatch,
            }, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(13, totalSteps, StepNames[12]));
        var validatedManifest = await _validateManifest.ExecuteAsync(manifest, ct);
        ct.ThrowIfCancellationRequested();

        progress?.Report(new StepProgress(14, totalSteps, StepNames[13]));
        var manifestPath = await _writeManifest.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = validatedManifest,
                ModsyncOutputPath = snapshot.ModsyncOutputPath,
            }, ct);

        _logger.LogInformation("=== Modsync Manager pack finished ===");
        _logger.LogInformation("Manifest: {Path}", manifestPath);

        return new PackResult
        {
            Config = config,
            Snapshot = snapshot,
            ArchiveIndex = archiveIndex,
            ModScan = modScan,
            Match = match,
            Manifest = validatedManifest,
            ManifestPath = manifestPath,
        };
    }

    private ArchiveIndex AddMo2ArchiveToResolved(
        PackConfig config,
        ArchiveIndex archiveIndex)
    {
        var mo2Archive = Mo2ArchiveBuilder.Build(
            config, archiveIndex, _logger);

        var resolved = archiveIndex.Resolved
            .Where(a => !string.Equals(
                a.Id, mo2Archive.Id, StringComparison.Ordinal))
            .Append(mo2Archive)
            .ToList();

        resolved.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        return new ArchiveIndex
        {
            Resolved = resolved,
            Unresolved = archiveIndex.Unresolved,
        };
    }

    private void WriteUnmatchedEntries(
        IReadOnlyList<UnmatchedEntry> entries,
        string sourceRoot,
        string outputRoot,
        string logContext,
        string[] keepNames)
    {
        if (entries.Count == 0)
            return;

        CleanDirectoryExcept(outputRoot, keepNames);

        Directory.CreateDirectory(outputRoot);

        int written = 0;

        foreach (var entry in entries)
        {
            var relativePath = entry.RelativePath
                .Replace('/', Path.DirectorySeparatorChar);

            var srcPath = Path.Combine(sourceRoot, relativePath);
            var dstPath = Path.Combine(outputRoot, relativePath);

            try
            {
                var dstDir = Path.GetDirectoryName(dstPath);
                if (!string.IsNullOrEmpty(dstDir))
                    Directory.CreateDirectory(dstDir);

                File.Copy(srcPath, dstPath, overwrite: true);
                written++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to write unmatched {Context} entry '{Path}' " +
                    "to __ModsyncManager_Output",
                    logContext, entry.RelativePath);
            }
        }

        _logger.LogInformation(
            "Unmatched {Context}: wrote {Count} file(s) to {Path}",
            logContext, written, outputRoot);
    }

    private void CleanDirectoryExcept(string path, string[] keepNames)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var name = Path.GetFileName(entry);

            if (keepNames.Length > 0 &&
                keepNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to clean '{Path}' during unmatched cleanup",
                    entry);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Input / Output
    // ------------------------------------------------------------------

    public sealed class Input
    {
        public required string ConfigPath { get; init; }
        public required ParallelOptions ParallelOptions { get; init; }
    }
}

public sealed class PackResult
{
    public required PackConfig Config { get; init; }
    public required InstanceSnapshot Snapshot { get; init; }
    public required ArchiveIndex ArchiveIndex { get; init; }
    public required ModScanResult ModScan { get; init; }
    public required MatchResult Match { get; init; }
    public required ModlistManifest Manifest { get; init; }
    public required string ManifestPath { get; init; }
}
