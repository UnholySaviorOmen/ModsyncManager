// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.MO2.Models;

/// <summary>
/// Содержимое modlist.txt: плоский список записей в порядке файла.
/// </summary>
public sealed class ModlistFile
{
    public required IReadOnlyList<ModlistEntry> Entries { get; init; }

    /// <summary>Все записи без сепараторов (Enabled не важен).</summary>
    public IEnumerable<ModlistEntry> AllMods => Entries;
}
