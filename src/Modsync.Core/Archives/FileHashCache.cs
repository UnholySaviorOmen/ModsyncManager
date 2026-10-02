// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Archives;

/// <summary>
/// In-memory кеш хешей файлов.
///
/// Ключ — (полный путь, размер, время последней записи UTC).
/// Если файл не менялся — хеш берётся из кеша.
///
/// Не персистентный: словарь живёт только в RAM, умирает при выходе
/// процесса. Для persist — SqliteHashCache.
/// </summary>
public sealed class FileHashCache : IHashCache
{
    private readonly ConcurrentDictionary<FileKey, XxHash64Value> _cache = new();

    public XxHash64Value GetOrCompute(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"File not found: {path}", path);

        var key = new FileKey(path, info.Length, info.LastWriteTimeUtc);

        return _cache.GetOrAdd(key, _ => XxHash64Value.FromFile(path));
    }

    public int Count => _cache.Count;

    public void Clear() => _cache.Clear();

    private readonly record struct FileKey(string Path, long Length, DateTime LastWriteUtc);
}
