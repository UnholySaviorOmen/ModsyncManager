// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install;
using Modsync.Install.Steps;

namespace Modsync.Install.Tests;

public class InstallSummaryBuilderTests
{
    private static InstallPipeline.Output MakeOutput(
        string instancePath = "/test/instance",
        IReadOnlyList<string>? archiveAlreadyPresent = null,
        IReadOnlyList<string>? archiveDownloaded = null,
        IReadOnlyList<string>? archiveSkipped = null,
        IReadOnlyList<string>? modsCreated = null,
        IReadOnlyList<string>? modsRecreated = null,
        IReadOnlyList<string>? modsSkipped = null,
        IReadOnlyList<string>? modsDeleted = null,
        IReadOnlyList<string>? metaIniWritten = null,
        IReadOnlyList<string>? metaIniDeleted = null,
        IReadOnlyList<string>? extWritten = null,
        IReadOnlyList<string>? extSkipped = null,
        IReadOnlyList<string>? xstWritten = null,
        IReadOnlyList<string>? xstSkipped = null,
        int profileMods = 0,
        int profilePlugins = 0,
        int profileLoadorder = 0)
    {
        var manifest = new ModlistManifest
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test Pack",
                Version = "1.2.3",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Execution = new ExecutionPolicy(),
            Mo2 = new Mo2Section
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = new ArchiveEntry
                {
                    Id = "mo2",
                    Name = "MO2.7z",
                    Size = 0,
                    Hash = new XxHash64Value(0),
                    Sources = Array.Empty<ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

        return new InstallPipeline.Output
        {
            InstancePath = instancePath,
            Manifest = manifest,
            BootstrapInstance = new BootstrapInstanceStep.Output
            {
                InstancePath = instancePath,
                Mo2Path = Path.Combine(instancePath, "MO2"),
                DownloadsPath = Path.Combine(instancePath, "MO2", "downloads"),
                ModsPath = Path.Combine(instancePath, "MO2", "mods"),
                ProfilesPath = Path.Combine(instancePath, "MO2", "profiles"),
                PluginsPath = Path.Combine(instancePath, "MO2", "plugins"),
                ToolsPath = Path.Combine(instancePath, "MO2", "tools"),
                StockGamePath = Path.Combine(instancePath, "Stock Game"),
                ManifestPathInInstance = Path.Combine(instancePath, "modlist.json"),
            },
            SyncArchives = new SyncArchivesStep.Output
            {
                AlreadyPresent = archiveAlreadyPresent ?? Array.Empty<string>(),
                Downloaded = archiveDownloaded ?? Array.Empty<string>(),
                Skipped = archiveSkipped ?? Array.Empty<string>(),
            },
            ExecuteExtensions = new ExecuteExtensionsStep.Output
            {
                Written = extWritten ?? Array.Empty<string>(),
                Skipped = extSkipped ?? Array.Empty<string>(),
            },
            ExecuteExtras = new ExecuteExtrasStep.Output
            {
                Written = xstWritten ?? Array.Empty<string>(),
                Skipped = xstSkipped ?? Array.Empty<string>(),
            },
            SyncMods = new SyncModsStep.Output
            {
                Created = modsCreated ?? Array.Empty<string>(),
                Recreated = modsRecreated ?? Array.Empty<string>(),
                Skipped = modsSkipped ?? Array.Empty<string>(),
                Deleted = modsDeleted ?? Array.Empty<string>(),
            },
            GenerateMetaIni = new GenerateMetaIniStep.Output
            {
                Written = metaIniWritten ?? Array.Empty<string>(),
                Deleted = metaIniDeleted ?? Array.Empty<string>(),
            },
            RegenerateProfile = new RegenerateProfileStep.Output
            {
                ProfilePath = Path.Combine(instancePath, "MO2", "profiles", "Default"),
                ModlistPath = Path.Combine(instancePath, "MO2", "profiles", "Default", "modlist.txt"),
                PluginsPath = Path.Combine(instancePath, "MO2", "profiles", "Default", "plugins.txt"),
                LoadorderPath = Path.Combine(instancePath, "MO2", "profiles", "Default", "loadorder.txt"),
                ModlistCount = profileMods,
                PluginsCount = profilePlugins,
                LoadorderCount = profileLoadorder,
            },
        };
    }

    [Fact]
    public void Build_MinimalOutput_CopiesAllScalars()
    {
        var output = MakeOutput();
        var summary = InstallSummaryBuilder.Build(output);

        summary.Name.Should().Be("Test Pack");
        summary.Version.Should().Be("1.2.3");
        summary.Game.Should().Be("skyrimspecialedition");
        summary.InstancePath.Should().Be("/test/instance");
        summary.ManifestPathInInstance.Should().Be(
            Path.Combine("/test/instance", "modlist.json"));

        summary.ArchivesAlreadyPresent.Should().Be(0);
        summary.ArchivesDownloaded.Should().Be(0);
        summary.ArchivesSkipped.Should().Be(0);

        summary.ModsCreated.Should().Be(0);
        summary.ModsRecreated.Should().Be(0);
        summary.ModsSkipped.Should().Be(0);
        summary.ModsDeleted.Should().Be(0);

        summary.MetaIniWritten.Should().Be(0);
        summary.MetaIniDeleted.Should().Be(0);

        summary.ExtensionsWritten.Should().Be(0);
        summary.ExtensionsSkipped.Should().Be(0);

        summary.ExtrasWritten.Should().Be(0);
        summary.ExtrasSkipped.Should().Be(0);

        summary.ProfileMods.Should().Be(0);
        summary.ProfilePlugins.Should().Be(0);
        summary.ProfileLoadorder.Should().Be(0);
    }

    [Fact]
    public void Build_CountsListsFromAllSections()
    {
        var output = MakeOutput(
            archiveAlreadyPresent: new[] { "a1", "a2" },
            archiveDownloaded: new[] { "a3" },
            archiveSkipped: new[] { "a4", "a5", "a6" },
            modsCreated: new[] { "m1", "m2" },
            modsRecreated: new[] { "m3" },
            modsSkipped: new[] { "m4" },
            modsDeleted: new[] { "m5", "m6", "m7", "m8" },
            metaIniWritten: new[] { "meta1" },
            metaIniDeleted: new[] { "meta2", "meta3" },
            extWritten: new[] { "e1", "e2" },
            extSkipped: new[] { "e3" },
            xstWritten: new[] { "x1" },
            xstSkipped: new[] { "x2", "x3" },
            profileMods: 42,
            profilePlugins: 12,
            profileLoadorder: 7);

        var summary = InstallSummaryBuilder.Build(output);

        summary.ArchivesAlreadyPresent.Should().Be(2);
        summary.ArchivesDownloaded.Should().Be(1);
        summary.ArchivesSkipped.Should().Be(3);

        summary.ModsCreated.Should().Be(2);
        summary.ModsRecreated.Should().Be(1);
        summary.ModsSkipped.Should().Be(1);
        summary.ModsDeleted.Should().Be(4);

        summary.MetaIniWritten.Should().Be(1);
        summary.MetaIniDeleted.Should().Be(2);

        summary.ExtensionsWritten.Should().Be(2);
        summary.ExtensionsSkipped.Should().Be(1);

        summary.ExtrasWritten.Should().Be(1);
        summary.ExtrasSkipped.Should().Be(2);

        summary.ProfileMods.Should().Be(42);
        summary.ProfilePlugins.Should().Be(12);
        summary.ProfileLoadorder.Should().Be(7);
    }

    [Fact]
    public void Build_NullOutput_Throws()
    {
        var act = () => InstallSummaryBuilder.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
