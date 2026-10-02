// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Archives;

/// <summary>
/// Кеш хешей файлов.
///
/// Ключ: (path, length, mtime_utc). Если файл не менялся — все три
/// поля совпадают, хеш берётся из кеша. Если менялся — пересчёт.
///
/// Реализации:
///   - FileHashCache   — in-memory (ConcurrentDictionary).
///   - SqliteHashCache — L1 (in-memory) + L2 (SQLite).
///
/// Контракт одинаковый: GetOrCompute возвращает хеш, кешируя его.
/// Clear очищает кеш (для SqliteHashCache — таблицу в БД).
/// </summary>
public interface IHashCache
{
    /// <summary>
    /// Возвращает хеш файла. Если файл уже в кеше и не менялся
    /// (совпадают length и mtime) — берёт из кеша. Иначе считает
    /// и кеширует.
    ///
    /// Бросает FileNotFoundException, если файла нет.
    /// </summary>
    XxHash64Value GetOrCompute(string path);

    /// <summary>
    /// Количество записей в кеше.
    /// Для SqliteHashCache — только L1 (in-memory).
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Очищает кеш.
    /// Для SqliteHashCache — L1 + L2 (таблица в SQLite).
    /// </summary>
    void Clear();
}
