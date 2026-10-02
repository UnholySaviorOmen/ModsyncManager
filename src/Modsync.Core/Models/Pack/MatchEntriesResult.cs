// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest.Directives;

namespace Modsync.Core.Models.Pack;

/// <summary>
/// Результат MatchExtensionsStep / MatchExtrasStep.
///
/// Отличие от MatchResult:
///   - MatchResult:      ключи — имена модов (из modlist.txt);
///                       Unmatched — UnmatchedFile (ModName + RelativePath).
///   - MatchEntriesResult: ключи — entryName (путь из config, например
///                       "tools/BethINI");
///                       Unmatched — UnmatchedEntry (EntryName + RelativePath).
///
/// Разные семантики — разные типы.
/// </summary>
public sealed class MatchEntriesResult
{
    public required IReadOnlyDictionary<string, IReadOnlyList<Directive>> Directives { get; init; }
    public required IReadOnlyList<UnmatchedEntry> Unmatched { get; init; }
}
