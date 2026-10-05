// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Install;

/// <summary>
/// Строит <see cref="InstallSummary"/> из <see cref="InstallPipeline.Output"/>.
///
/// Единственная точка, где решается «что показывать пользователю».
/// CLI и GUI используют один и тот же Summary.
///
/// Статический класс: без состояния, без DI.
/// </summary>
public static class InstallSummaryBuilder
{
    public static InstallSummary Build(InstallPipeline.Output output)
    {
        if (output is null) throw new ArgumentNullException(nameof(output));

        var manifest = output.Manifest;

        return new InstallSummary
        {
            Name = manifest.Meta.Name,
            Version = manifest.Meta.Version,
            Game = manifest.Meta.Game,

            InstancePath = output.InstancePath,
            ManifestPathInInstance = Path.Combine(
                output.InstancePath, "modlist.json"),

            ArchivesAlreadyPresent = output.SyncArchives.AlreadyPresent.Count,
            ArchivesDownloaded = output.SyncArchives.Downloaded.Count,
            ArchivesSkipped = output.SyncArchives.Skipped.Count,

            ModsCreated = output.SyncMods.Created.Count,
            ModsRecreated = output.SyncMods.Recreated.Count,
            ModsSkipped = output.SyncMods.Skipped.Count,
            ModsDeleted = output.SyncMods.Deleted.Count,

            MetaIniWritten = output.GenerateMetaIni.Written.Count,
            MetaIniDeleted = output.GenerateMetaIni.Deleted.Count,

            ArchiveMetaWritten = output.GenerateArchiveMeta.Written.Count,
            ArchiveMetaSkipped = output.GenerateArchiveMeta.Skipped.Count,

            ExtensionsWritten = output.ExecuteExtensions.Written.Count,
            ExtensionsSkipped = output.ExecuteExtensions.Skipped.Count,

            ExtrasWritten = output.ExecuteExtras.Written.Count,
            ExtrasSkipped = output.ExecuteExtras.Skipped.Count,

            ProfileMods = output.RegenerateProfile.ModlistCount,
            ProfilePlugins = output.RegenerateProfile.PluginsCount,
            ProfileLoadorder = output.RegenerateProfile.LoadorderCount,
        };
    }
}
