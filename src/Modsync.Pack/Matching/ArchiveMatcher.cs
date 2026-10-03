// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Pack;
using Microsoft.Extensions.Logging;

namespace Modsync.Pack.Matching;

/// <summary>
/// Построение индексов архивов и матчинг файлов по хешу.
///
/// Используется MatchStep, MatchExtensionsStep, MatchExtrasStep.
///
/// Жизненный цикл:
///   1. Создать объект.
///   2. Вызвать BuildAsync(ct, progress) — распаковывает все
///      Resolved-архивы, строит byHashPath/byHash/byPath.
///   3. Многократно вызывать TryMatch(file).
///
/// Прогресс: если передан IProgress&lt;(int, int)&gt;, репортит
/// (processed, total) после каждого распакованного архива.
/// </summary>
internal sealed class ArchiveMatcher
{
    private readonly ArchiveIndex _archiveIndex;
    private readonly string _downloadsPath;
    private readonly IArchiveExtractor _extractor;
    private readonly FileHashCache _hashCache;
    private readonly ILogger<ArchiveMatcher> _logger;

    private ArchiveIndexes? _indexes;

    public ArchiveMatcher(
        ArchiveIndex archiveIndex,
        string downloadsPath,
        IArchiveExtractor extractor,
        FileHashCache hashCache,
        ILogger<ArchiveMatcher> logger)
    {
        _archiveIndex = archiveIndex;
        _downloadsPath = downloadsPath;
        _extractor = extractor;
        _hashCache = hashCache;
        _logger = logger;
    }

    /// <summary>
    /// Построить индексы. Повторный вызов — no-op.
    ///
    /// progress — опциональный репорт (processed, total) после
    /// каждого архива.
    ///
    /// При отмене бросает OperationCanceledException как есть.
    /// </summary>
    public async Task BuildAsync(
        CancellationToken ct,
        IProgress<(int Processed, int Total)>? progress = null)
    {
        if (_indexes is not null)
            return;

        var byHashPath = new Dictionary<
            (XxHash64Value, string), IndexEntry>();
        var byHash = new Dictionary<XxHash64Value, List<IndexEntry>>();
        var byPath = new Dictionary<
            string, Dictionary<XxHash64Value, string>>(
            StringComparer.OrdinalIgnoreCase);

        int processed = 0;
        int total = _archiveIndex.Resolved.Count;

        // Стартовый репорт: 0 / N.
        progress?.Report((0, total));

        foreach (var archive in _archiveIndex.Resolved)
        {
            ct.ThrowIfCancellationRequested();

            processed++;
            if (processed % 10 == 0 || processed == total)
            {
                _logger.LogInformation(
                    "ArchiveMatcher: extracting {Current}/{Total}: {Name}",
                    processed, total, archive.Name);
            }

            var archivePath = Path.Combine(_downloadsPath, archive.Name);

            if (!File.Exists(archivePath))
            {
                _logger.LogWarning(
                    "ArchiveMatcher: archive file not found (skipping): {Path}",
                    archivePath);
                progress?.Report((processed, total));
                continue;
            }

            using var workspace = new TempWorkspace();

            IReadOnlyList<string> extractedFiles;
            try
            {
                extractedFiles = await _extractor.ExtractAsync(
                    archivePath, workspace.Path, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ArchiveMatcher: failed to extract '{Name}'. Skipping.",
                    archive.Name);
                progress?.Report((processed, total));
                continue;
            }

            foreach (var relativePath in extractedFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fullPath = Path.Combine(
                    workspace.Path,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));

                XxHash64Value hash;
                long size;
                try
                {
                    hash = _hashCache.GetOrCompute(fullPath);
                    size = new FileInfo(fullPath).Length;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "ArchiveMatcher: failed to hash '{Path}' from '{Archive}'",
                        relativePath, archive.Name);
                    continue;
                }

                var entry = new IndexEntry
                {
                    ArchiveId = archive.Id,
                    RelativePath = relativePath,
                    Size = size,
                };

                var key = (hash, relativePath);
                if (byHashPath.TryGetValue(key, out var existingExact))
                {
                    if (string.CompareOrdinal(entry.ArchiveId, existingExact.ArchiveId) < 0)
                        byHashPath[key] = entry;
                }
                else
                {
                    byHashPath[key] = entry;
                }

                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<IndexEntry>(1);
                    byHash[hash] = list;
                }
                list.Add(entry);

                if (!byPath.TryGetValue(relativePath, out var hashesByPath))
                {
                    hashesByPath = new Dictionary<XxHash64Value, string>();
                    byPath[relativePath] = hashesByPath;
                }

                if (hashesByPath.TryGetValue(hash, out var existingArchiveId))
                {
                    if (string.CompareOrdinal(archive.Id, existingArchiveId) < 0)
                        hashesByPath[hash] = archive.Id;
                }
                else
                {
                    hashesByPath[hash] = archive.Id;
                }
            }

            progress?.Report((processed, total));
        }

        foreach (var list in byHash.Values)
        {
            list.Sort(static (a, b) =>
            {
                var c = string.CompareOrdinal(a.ArchiveId, b.ArchiveId);
                if (c != 0) return c;
                return string.CompareOrdinal(a.RelativePath, b.RelativePath);
            });
        }

        _indexes = new ArchiveIndexes(byHashPath, byHash, byPath);

        _logger.LogInformation(
            "ArchiveMatcher: built. {Hashes} unique hashes, " +
            "{HashPaths} unique (hash,path), {Paths} unique paths",
            byHash.Count, byHashPath.Count, byPath.Count);
    }

    public FromArchiveDirective? TryMatch(ScannedFile file)
    {
        if (_indexes is null)
            throw new InvalidOperationException(
                "ArchiveMatcher.BuildAsync must be called before TryMatch.");

        if (_indexes.ByHashPath.TryGetValue(
                (file.Hash, file.RelativePath), out var exact))
        {
            return new FromArchiveDirective
            {
                Archive = exact.ArchiveId,
                Source = exact.RelativePath,
                Destination = file.RelativePath,
                Hash = file.Hash,
                Size = file.Size,
            };
        }

        if (_indexes.ByHash.TryGetValue(file.Hash, out var candidates)
            && candidates.Count > 0)
        {
            var pick = candidates[0];
            return new FromArchiveDirective
            {
                Archive = pick.ArchiveId,
                Source = pick.RelativePath,
                Destination = file.RelativePath,
                Hash = file.Hash,
                Size = file.Size,
            };
        }

        return null;
    }

    internal sealed class IndexEntry
    {
        public required string ArchiveId { get; init; }
        public required string RelativePath { get; init; }
        public required long Size { get; init; }
    }
}
