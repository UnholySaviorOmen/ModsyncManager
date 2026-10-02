// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Результат сканирования папки mods/.
/// Ключ — имя мода (из modlist.txt), значение — список файлов.
/// </summary>
public sealed class ModScanResult
{
    /// <summary>Моды и их отсканированные файлы.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<ScannedFile>> Mods { get; init; }

    /// <summary>Общее количество файлов во всех модах.</summary>
    public int TotalFiles => Mods.Values.Sum(f => f.Count);

    /// <summary>Количество модов.</summary>
    public int TotalMods => Mods.Count;
}

/// <summary>
/// Один файл внутри мода.
/// </summary>
public sealed class ScannedFile
{
    /// <summary>Путь относительно корня мода, с прямыми слэшами.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Хеш файла (xxHash64).</summary>
    public required XxHash64Value Hash { get; init; }

    /// <summary>Размер файла в байтах.</summary>
    public required long Size { get; init; }
}
