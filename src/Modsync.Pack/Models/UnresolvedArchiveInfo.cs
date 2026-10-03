// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;

namespace Modsync.Pack.Models;

/// <summary>
/// Информация об архиве в downloads/, для которого packer не может
/// определить источник автоматически (нет .meta-файла).
///
/// Используется GUI-формой «Create Pack Config»: показывает список
/// таких архивов пользователю, чтобы он вручную указал URL + hash.
///
/// Хеш архива вычисляется при сканировании и показывается в форме —
/// пользователь может скопировать его в поле «hash» при заполнении.
/// </summary>
public sealed record UnresolvedArchiveInfo
{
    /// <summary>Имя файла архива (без пути). Например, "AnotherMod.7z".</summary>
    public required string FileName { get; init; }

    /// <summary>Полный путь к архиву в downloads/.</summary>
    public required string FullPath { get; init; }

    /// <summary>Размер файла в байтах.</summary>
    public required long Size { get; init; }

    /// <summary>xxHash64 архива целиком.</summary>
    public required XxHash64Value Hash { get; init; }
}
