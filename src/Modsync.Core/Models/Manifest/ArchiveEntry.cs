// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Core.Models.Manifest;

/// <summary>
/// Запись архива в манифесте.
/// </summary>
public sealed class ArchiveEntry
{
    /// <summary>
    /// Канонический id архива.
    /// Для Nexus: "nexus_{game_domain}_{modId}_{fileId}".
    /// Для локальных (без .meta): "local_{slug}".
    /// Для GitHub: "github_{owner}_{repo}_{tag}_{assetSlug}".
    /// Уникален в пределах манифеста.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Имя файла архива в downloads/ (например, "SkyUI_5_1-3863-5-1.7z").
    /// Информационное поле — используется для отображения и как fallback.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Размер архива в байтах.</summary>
    public required long Size { get; init; }

    /// <summary>Хеш архива (xxHash64).</summary>
    public required XxHash64Value Hash { get; init; }

    /// <summary>
    /// Источники для скачивания, в порядке приоритета.
    /// Не пустой список — как минимум один источник обязателен.
    /// </summary>
    public required IReadOnlyList<ArchiveSourceRef> Sources { get; init; }
}
