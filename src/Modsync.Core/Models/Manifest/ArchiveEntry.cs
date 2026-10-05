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

    /// <summary>
    /// Structured содержимое .meta-файла MO2 (секция [General]).
    ///
    /// Null — .meta не нужен (архив не с Nexus) или его не было
    /// в исходном downloads/. Installer в этом случае .meta не создаёт.
    /// Не null — installer должен создать
    /// downloads/&lt;Name&gt;.meta рядом с архивом.
    ///
    /// Packer заполняет это поле, если рядом с архивом в downloads/
    /// есть валидный .meta (modID + fileID). Installer восстанавливает
    /// .meta на целевой машине, чтобы последующий pack снова видел
    /// архив как nexus-архив.
    ///
    /// Поле опциональное: в JSON не пишется, если null. Старые
    /// манифесты (без этого поля) читаются как Meta = null.
    /// </summary>
    public ModMeta? Meta { get; init; }
}
