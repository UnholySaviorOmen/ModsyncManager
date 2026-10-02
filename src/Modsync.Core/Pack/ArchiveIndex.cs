// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Результат индексации downloads/.
/// </summary>
public sealed class ArchiveIndex
{
    /// <summary>Архивы с определённым источником. Готовы к манифесту.</summary>
    public required IReadOnlyList<ArchiveEntry> Resolved { get; init; }

    /// <summary>
    /// Архивы без .meta и без archiveSources. Их источник неизвестен.
    /// Возможно, они используются модами — тогда это ошибка.
    /// Возможно, это мусор — тогда игнорируем.
    /// Решение принимает MatchStep.
    /// </summary>
    public required IReadOnlyList<UnresolvedArchive> Unresolved { get; init; }
}

/// <summary>Архив, для которого не удалось определить источник.</summary>
public sealed class UnresolvedArchive
{
    /// <summary>Полный путь к файлу.</summary>
    public required string FullPath { get; init; }

    /// <summary>Имя файла (без пути).</summary>
    public required string FileName { get; init; }

    /// <summary>Размер в байтах.</summary>
    public required long Size { get; init; }

    /// <summary>Хеш архива.</summary>
    public required Models.Hashing.XxHash64Value Hash { get; init; }
}
