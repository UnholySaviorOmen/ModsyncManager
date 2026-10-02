// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Hashing;

namespace Modsync.Pack.Matching;

/// <summary>
/// Три индекса, построенные ArchiveMatcher-ом.
/// Тот же набор, что в MatchStep, но вынесен в отдельный тип.
/// </summary>
internal sealed record ArchiveIndexes(
    Dictionary<(XxHash64Value, string), ArchiveMatcher.IndexEntry> ByHashPath,
    Dictionary<XxHash64Value, List<ArchiveMatcher.IndexEntry>> ByHash,
    Dictionary<string, Dictionary<XxHash64Value, string>> ByPath);
