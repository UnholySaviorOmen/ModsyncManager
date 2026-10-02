// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core.Models.Mo2;

public sealed class ModlistFile
{
    public required IReadOnlyList<ModlistEntry> Entries { get; init; }
    public IEnumerable<ModlistEntry> AllMods => Entries;
}
