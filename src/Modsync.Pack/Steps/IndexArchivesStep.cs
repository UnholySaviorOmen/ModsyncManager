// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Identity;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Platform.MO2.Readers;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Steps;

/// <summary>
/// Индексирует downloads/.
/// Для каждого архива определяет источник через .meta или archiveSources.
/// Считает хеши архивов (через IHashCache: L1 + L2).
/// НЕ распаковывает архивы.
/// </summary>
public sealed class IndexArchivesStep
    : IStep<IndexArchivesStep.Input, ArchiveIndex>
{
    private readonly IHashCache _hashCache;
    private readonly ILogger<IndexArchivesStep> _logger;

    public IndexArchivesStep(IHashCache hashCache, ILogger<IndexArchivesStep> logger)
    {
        _hashCache = hashCache;
        _logger = logger;
    }

    public Task<ArchiveIndex> ExecuteAsync(Input input, CancellationToken ct)
    {
        if (!Directory.Exists(input.DownloadsPath))
            throw new DirectoryNotFoundException(
                $"downloads/ not found: {input.DownloadsPath}");

        var files = Directory.EnumerateFiles(input.DownloadsPath)
            .Where(ArchiveExtensions.IsArchive)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "Indexing {Count} archives in {Path}", files.Count, input.DownloadsPath);

        var perFileResults = new ConcurrentBag<PerFileResult>();

        Parallel.ForEach(
            files,
            input.ParallelOptions,
            () => new List<PerFileResult>(),
            (file, _, localList) =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var result = ProcessOneFile(file, input, ct);
                    localList.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process archive: {File}", file);
                    throw;
                }

                return localList;
            },
            localList =>
            {
                foreach (var r in localList)
                    perFileResults.Add(r);
            });

        var resolved = new List<ArchiveEntry>();
        var unresolved = new List<UnresolvedArchive>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in perFileResults)
        {
            switch (result.Kind)
            {
                case PerFileKind.Resolved:
                    var entry = result.Entry!;
                    if (!seenIds.Add(entry.Id))
                        throw new InvalidOperationException(
                            $"Duplicate archive id '{entry.Id}' " +
                            $"for file '{entry.Name}'. " +
                            $"Check .meta files and archiveSources.");
                    resolved.Add(entry);
                    break;

                case PerFileKind.Unresolved:
                    unresolved.Add(result.UnresolvedEntry!);
                    break;
            }
        }

        resolved.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        unresolved.Sort((a, b) => string.CompareOrdinal(a.FileName, b.FileName));

        _logger.LogInformation(
            "Indexed: {Resolved} resolved, {Unresolved} unresolved",
            resolved.Count, unresolved.Count);

        return Task.FromResult(new ArchiveIndex
        {
            Resolved = resolved,
            Unresolved = unresolved,
        });
    }

    private PerFileResult ProcessOneFile(string file, Input input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var fileName = Path.GetFileName(file);
        var metaPath = file + ".meta";

        // 1. Пробуем .meta
        if (File.Exists(metaPath))
        {
            ModMeta? meta;
            try
            {
                meta = MetaIniReader.TryRead(metaPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse .meta for '{File}'. " +
                    "Falling back to archiveSources.",
                    fileName);
                meta = null;
            }

            if (meta is not null
                && meta.ModId.HasValue
                && meta.FileId.HasValue)
            {
                var id = ArchiveId.FromNexus(
                    input.GameDomain, meta.ModId.Value, meta.FileId.Value);

                return PerFileResult.AsResolved(new ArchiveEntry
                {
                    Id = id,
                    Name = fileName,
                    Size = new FileInfo(file).Length,
                    Hash = _hashCache.GetOrCompute(file),
                    Sources = new ArchiveSourceRef[]
                    {
                        new NexusSourceRef
                        {
                            ModId  = meta.ModId.Value,
                            FileId = meta.FileId.Value,
                            Game   = input.GameDomain,
                        },
                    },
                    Meta = meta,
                });
            }

            _logger.LogWarning(
                "Ignoring non-Nexus .meta for '{File}' (no modID/fileID). " +
                "Falling back to archiveSources.",
                fileName);
        }

        // 2. Пробуем archiveSources
        var explicitSource = input.Config.ArchiveSources
            .FirstOrDefault(s => string.Equals(
                s.Archive, fileName, StringComparison.OrdinalIgnoreCase));

        if (explicitSource is not null)
        {
            var id = ArchiveId.FromLocal(fileName);

            return PerFileResult.AsResolved(new ArchiveEntry
            {
                Id = id,
                Name = fileName,
                Size = new FileInfo(file).Length,
                Hash = _hashCache.GetOrCompute(file),
                Sources = explicitSource.Sources,
            });
        }

        // 3. Unresolved
        _logger.LogWarning(
            "Archive has no valid .meta and no entry in archiveSources: {File}",
            fileName);

        return PerFileResult.AsUnresolved(new UnresolvedArchive
        {
            FullPath = file,
            FileName = fileName,
            Size = new FileInfo(file).Length,
            Hash = _hashCache.GetOrCompute(file),
        });
    }

    public sealed class Input
    {
        public required string DownloadsPath { get; init; }
        public required PackConfig Config { get; init; }
        public required string GameDomain { get; init; }
        public required ParallelOptions ParallelOptions { get; init; }
    }

    private enum PerFileKind { Resolved, Unresolved }

    private sealed class PerFileResult
    {
        public PerFileKind Kind { get; private init; }
        public ArchiveEntry? Entry { get; private init; }
        public UnresolvedArchive? UnresolvedEntry { get; private init; }

        public static PerFileResult AsResolved(ArchiveEntry entry)
            => new() { Kind = PerFileKind.Resolved, Entry = entry };

        public static PerFileResult AsUnresolved(UnresolvedArchive u)
            => new() { Kind = PerFileKind.Unresolved, UnresolvedEntry = u };
    }
}
