// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Pack;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Результат сканирования extensions или extras.
///
/// Структурно похож на ModScanResult, но семантика другая:
///   - ModScanResult:      ключ — имя мода из modlist.txt,
///                         RelativePath файла — ОТ КОРНЯ МОДА.
///   - EntryScanResult:    ключ — entry path из config (например, "tools/BethINI"),
///                         RelativePath файла — ОТ КОРНЯ MO2/ (для extensions)
///                         или Stock Game/ (для extras).
///
/// Разные семантики — разные типы. Не переиспользуем ModScanResult,
/// чтобы в MatchExtensionsStep/MatchExtrasStep не помнить
/// "а какой тут корень".
/// </summary>
public sealed class EntryScanResult
{
    /// <summary>
    /// EntryName (как в config.Mo2.Extensions[] / config.StockGame.Extras[]) → список файлов.
    /// Порядок entries — как в config. Файлы внутри — сортированы по RelativePath (Ordinal).
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<ScannedFile>> Entries { get; init; }

    public int TotalEntries => Entries.Count;

    public int TotalFiles => Entries.Values.Sum(f => f.Count);
}
