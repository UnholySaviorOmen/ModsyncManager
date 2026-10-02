// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Сканирует extras из config.StockGame.Extras[].
///
/// Каждый элемент массива — относительный путь ОТ КОРНЯ Stock Game/.
/// Это либо файл, либо папка.
///
/// Хеши берутся через IHashCache (L1 + L2). Пути (Stock Game/)
/// стабильны между прогонами → L2 работает.
///
/// Результат — EntryScanResult.
/// </summary>
public sealed class ScanExtrasStep
    : IStep<ScanExtrasStep.Input, EntryScanResult>
{
    private readonly IHashCache _hashCache;
    private readonly ILogger<ScanExtrasStep> _logger;

    public ScanExtrasStep(
        IHashCache hashCache,
        ILogger<ScanExtrasStep> logger)
    {
        _hashCache = hashCache;
        _logger = logger;
    }

    public Task<EntryScanResult> ExecuteAsync(Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var entries = input.Config.StockGame.Extras;
        var rootPath = input.Snapshot.StockGamePath;

        _logger.LogInformation(
            "ScanExtrasStep: scanning {Count} extra(s) from {Root}",
            entries.Count, rootPath);

        if (entries.Count == 0)
        {
            return Task.FromResult(new EntryScanResult
            {
                Entries = new Dictionary<string, IReadOnlyList<ScannedFile>>(
                    StringComparer.Ordinal),
            });
        }

        var results = new ConcurrentDictionary<string, IReadOnlyList<ScannedFile>>(
            StringComparer.Ordinal);

        Parallel.ForEach(
            entries,
            input.ParallelOptions,
            () => 0,
            (entry, _, _) =>
            {
                ct.ThrowIfCancellationRequested();

                var files = ScanEntry(rootPath, entry);

                results[entry] = files;

                _logger.LogDebug(
                    "Scanned extra '{Entry}': {Count} file(s)",
                    entry, files.Count);

                return 0;
            },
            _ => { });

        var ordered = new Dictionary<string, IReadOnlyList<ScannedFile>>(
            StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (results.TryGetValue(entry, out var files))
                ordered[entry] = files;
        }

        int totalFiles = ordered.Values.Sum(v => v.Count);

        _logger.LogInformation(
            "ScanExtrasStep: {Entries} entries, {Files} files total",
            ordered.Count, totalFiles);

        return Task.FromResult(new EntryScanResult
        {
            Entries = ordered,
        });
    }

    private IReadOnlyList<ScannedFile> ScanEntry(string rootPath, string entry)
    {
        var normalizedEntry = entry
            .Replace('\\', '/')
            .TrimEnd('/');

        var absolutePath = Path.Combine(
            rootPath,
            normalizedEntry.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(absolutePath))
        {
            var file = ScanSingleFile(
                absolutePath,
                relativePathFromRoot: normalizedEntry);

            return new[] { file };
        }

        if (Directory.Exists(absolutePath))
        {
            return ScanDirectory(
                absolutePath,
                entryPrefix: normalizedEntry);
        }

        throw new FileNotFoundException(
            $"Stock Game extra not found in instance: " +
            $"'{entry}' (resolved to '{absolutePath}'). " +
            $"Check config.stockGame.extras[] and the instance contents.",
            absolutePath);
    }

    private IReadOnlyList<ScannedFile> ScanDirectory(
        string absoluteDir,
        string entryPrefix)
    {
        var result = new List<ScannedFile>();

        var files = Directory.EnumerateFiles(
            absoluteDir,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
            });

        foreach (var file in files)
        {
            var relativeInside = Path
                .GetRelativePath(absoluteDir, file)
                .Replace('\\', '/');

            var relativeFromRoot = entryPrefix + "/" + relativeInside;

            result.Add(ScanSingleFile(file, relativeFromRoot));
        }

        result.Sort((a, b) =>
            string.CompareOrdinal(a.RelativePath, b.RelativePath));

        return result;
    }

    private ScannedFile ScanSingleFile(string absolutePath, string relativePathFromRoot)
    {
        var hash = _hashCache.GetOrCompute(absolutePath);
        var size = new FileInfo(absolutePath).Length;

        return new ScannedFile
        {
            RelativePath = relativePathFromRoot,
            Hash = hash,
            Size = size,
        };
    }

    public sealed class Input
    {
        public required PackConfig Config { get; init; }
        public required InstanceSnapshot Snapshot { get; init; }
        public required ParallelOptions ParallelOptions { get; init; }
    }
}
