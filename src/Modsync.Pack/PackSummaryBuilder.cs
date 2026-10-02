// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Models.Manifest.Directives;

namespace Modsync.Pack;

/// <summary>
/// Строит <see cref="PackSummary"/> из <see cref="PackResult"/>.
///
/// Единственная точка, где решается «что показывать пользователю».
/// CLI и GUI используют один и тот же Summary.
///
/// Статический класс: без состояния, без DI.
/// </summary>
public static class PackSummaryBuilder
{
    public static PackSummary Build(PackResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));

        // Считаем директивы один раз.
        int totalDirectives = 0;
        int fromArchiveCount = 0;
        foreach (var (_, directives) in result.Match.ModDirectives)
        {
            foreach (var d in directives)
            {
                totalDirectives++;
                if (d is FromArchiveDirective) fromArchiveCount++;
            }
        }

        string? unmatchedWrittenTo = result.Match.Unmatched.Count > 0
            ? Path.Combine(result.Snapshot.ModsyncOutputPath, "mods")
            : null;

        return new PackSummary
        {
            Name = result.Config.Meta.Name,
            Version = result.Config.Meta.Version,
            Game = result.Config.Meta.Game,

            InstancePath = result.Snapshot.InstancePath,

            ModsTotal = result.Snapshot.Modlist.Entries.Count,
            PluginsTotal = result.Snapshot.Plugins.Entries.Count,
            LoadorderTotal = result.Snapshot.Loadorder.Plugins.Count,

            ArchivesResolved = result.ArchiveIndex.Resolved.Count,
            ArchivesUnresolved = result.ArchiveIndex.Unresolved.Count,

            ModsScanned = result.ModScan.TotalMods,
            FilesScanned = result.ModScan.TotalFiles,

            DirectivesTotal = totalDirectives,
            DirectivesFromArchive = fromArchiveCount,

            UnmatchedFiles = result.Match.Unmatched.Count,
            MetaIniCount = result.Match.ModMetas.Count,

            ManifestMods = result.Manifest.Mods.Count,
            ManifestArchives = result.Manifest.Archives.Count,
            ManifestExtensions = result.Manifest.Mo2.Extensions.Count,
            ManifestExtras = result.Manifest.StockGame.Extras.Count,

            UnmatchedWrittenTo = unmatchedWrittenTo,
            ManifestPath = result.ManifestPath,
        };
    }
}
