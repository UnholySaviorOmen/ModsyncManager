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
/// Используется MatchStep, MatchExtensionsStep, MatchExtrasStep (12.13.6).
///
/// Жизненный цикл:
///   1. Создать объект (передать ArchiveIndex + DownloadsPath + extractor).
///   2. Вызвать BuildAsync(ct) — распаковывает все Resolved-архивы,
///      строит byHashPath/byHash/byPath.
///   3. Многократно вызывать TryMatch(file) — вернёт директиву или null.
///
/// Индексы:
///   ByHashPath — точное совпадение (hash + relativePath) → entry.
///   ByHash     — совпадение только по hash. Значение — список кандидатов,
///                отсортированный по (archiveId, relativePath) для
///                детерминизма.
///   ByPath     — relativePath → {hash → archiveId}.
///
/// Детерминизм при дубликатах:
///   Если в разных архивах есть одинаковые (hash, relativePath) —
///   побеждает минимальный archiveId (Ordinal). Аналогично для
///   ByPath[path][hash]. Это гарантирует, что результат не зависит
///   от порядка обхода Resolved-массивов.
///
/// Правило матчинга:
///   1. Если (hash, relativePath) есть в ByHashPath → Exact.
///   2. Иначе если hash есть в ByHash → ByHash (первый кандидат).
///   3. Иначе → null.
///
/// Отмена:
///   OperationCanceledException из ExtractAsync пробрасывается наружу
///   без обёртки — BuildAsync не «глотает» отмену, а прерывает
///   весь pipeline. Прочие ошибки (битый архив, ошибка 7z) — skip
///   с логированием.
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
    /// Построить индексы. Можно вызывать один раз; повторный вызов — no-op.
    /// Extract-ит все Resolved-архивы.
    ///
    /// При отмене бросает OperationCanceledException как есть — pipeline
    /// должен прерваться.
    /// </summary>
    public async Task BuildAsync(CancellationToken ct)
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
                // Отмена — не «ошибка распаковки». Прерываем Build.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ArchiveMatcher: failed to extract '{Name}'. Skipping.",
                    archive.Name);
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

                // ByHashPath: детерминизм — минимальный archiveId.
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

                // ByHash: список кандидатов (сортируется в конце).
                if (!byHash.TryGetValue(hash, out var list))
                {
                    list = new List<IndexEntry>(1);
                    byHash[hash] = list;
                }
                list.Add(entry);

                // ByPath: детерминизм — минимальный archiveId.
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
        }

        // Детерминизм: сортируем списки кандидатов.
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

    /// <summary>
    /// Матчит файл. Возвращает директиву или null.
    /// Требует, чтобы BuildAsync был вызван.
    /// </summary>
    public FromArchiveDirective? TryMatch(ScannedFile file)
    {
        if (_indexes is null)
            throw new InvalidOperationException(
                "ArchiveMatcher.BuildAsync must be called before TryMatch.");

        // 1. Точное совпадение (hash, path).
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

        // 2. Совпадение по hash — первый кандидат.
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

        // 3. Нет матча.
        return null;
    }

    // ------------------------------------------------------------------
    //  Внутренние типы
    // ------------------------------------------------------------------

    internal sealed class IndexEntry
    {
        public required string ArchiveId { get; init; }
        public required string RelativePath { get; init; }
        public required long Size { get; init; }
    }
}
