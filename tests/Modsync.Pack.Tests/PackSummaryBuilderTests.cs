// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Mo2;
using Modsync.Core.Models.Pack;
using Modsync.Pack;

// Коллизия имён: Modsync.Core.Models.Manifest.PluginEntry
// и Modsync.Core.Models.Mo2.PluginEntry. Разводим псевдонимами.
using ManifestPluginEntry = Modsync.Core.Models.Manifest.PluginEntry;
using Mo2PluginEntry = Modsync.Core.Models.Mo2.PluginEntry;

namespace Modsync.Pack.Tests;

public class PackSummaryBuilderTests
{
    private static PackResult MakeResult(
        IReadOnlyList<ModlistEntry>? mods = null,
        IReadOnlyList<Mo2PluginEntry>? plugins = null,
        IReadOnlyList<string>? loadorder = null,
        IReadOnlyList<ArchiveEntry>? resolved = null,
        int unresolvedCount = 0,
        int modScanMods = 0,
        int modScanFiles = 0,
        Dictionary<string, IReadOnlyList<Directive>>? modDirectives = null,
        IReadOnlyList<UnmatchedFile>? unmatched = null,
        Dictionary<string, ModMeta>? modMetas = null,
        IReadOnlyList<ModEntry>? manifestMods = null,
        IReadOnlyList<ArchiveEntry>? manifestArchives = null,
        IReadOnlyList<ExtensionEntry>? manifestExtensions = null,
        IReadOnlyList<ExtensionEntry>? manifestExtras = null,
        string modsyncOutputPath = "/test/__ModsyncManager_Output",
        string manifestPath = "/test/__ModsyncManager_Output/modlist.json")
    {
        var mo2Archive = new ArchiveEntry
        {
            Id = "mo2",
            Name = "MO2.7z",
            Size = 0,
            Hash = new XxHash64Value(0),
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef
                {
                    Url = "https://example.com/MO2.7z",
                    Hash = new XxHash64Value(0),
                },
            },
        };

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
                Archive = mo2Archive,
                Extensions = manifestExtensions ?? Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = manifestExtras ?? Array.Empty<ExtensionEntry>(),
            },
            Archives = manifestArchives ?? Array.Empty<ArchiveEntry>(),
            Mods = manifestMods ?? Array.Empty<ModEntry>(),
            Plugins = Array.Empty<ManifestPluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

        var snapshot = new InstanceSnapshot
        {
            InstancePath = "/test/instance",
            Mo2Path = "/test/instance/MO2",
            DownloadsPath = "/test/instance/MO2/downloads",
            ModsPath = "/test/instance/MO2/mods",
            ProfilesPath = "/test/instance/MO2/profiles",
            StockGamePath = "/test/instance/Stock Game",
            ModsyncOutputPath = modsyncOutputPath,
            Modlist = new ModlistFile
            {
                Entries = mods ?? Array.Empty<ModlistEntry>(),
            },
            Plugins = new PluginsFile
            {
                Entries = plugins ?? Array.Empty<Mo2PluginEntry>(),
            },
            Loadorder = new LoadorderFile
            {
                Plugins = loadorder ?? Array.Empty<string>(),
            },
        };

        var archiveIndex = new ArchiveIndex
        {
            Resolved = resolved ?? Array.Empty<ArchiveEntry>(),
            Unresolved = Enumerable.Range(0, unresolvedCount)
                .Select(i => new UnresolvedArchive
                {
                    FullPath = "/test/downloads/x" + i + ".7z",
                    FileName = "x" + i + ".7z",
                    Size = 100,
                    Hash = new XxHash64Value((ulong)i),
                })
                .ToArray(),
        };

        var modScanDict = new Dictionary<string, IReadOnlyList<ScannedFile>>(
            StringComparer.Ordinal);
        for (int i = 0; i < modScanMods; i++)
        {
            var list = new List<ScannedFile>();
            for (int j = 0; j < (i == 0 ? modScanFiles : 0); j++)
            {
                list.Add(new ScannedFile
                {
                    RelativePath = $"f{j}.txt",
                    Hash = new XxHash64Value((ulong)j),
                    Size = 1,
                });
            }
            modScanDict["Mod" + i] = list;
        }

        var config = new PackConfig
        {
            Meta = new PackMeta
            {
                Name = "Test Pack",
                Version = "1.2.3",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Instance = new PackInstance { Path = "instance" },
            Mo2 = new PackMo2
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = "MO2.7z",
                Source = new MirrorSourceRef
                {
                    Url = "https://example.com/MO2.7z",
                    Hash = new XxHash64Value(0),
                },
                Extensions = Array.Empty<string>(),
            },
            StockGame = new PackStockGame { Extras = Array.Empty<string>() },
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        return new PackResult
        {
            Config = config,
            Snapshot = snapshot,
            ArchiveIndex = archiveIndex,
            ModScan = new ModScanResult { Mods = modScanDict },
            Match = new MatchResult
            {
                ModDirectives = modDirectives
                    ?? new Dictionary<string, IReadOnlyList<Directive>>(),
                Unmatched = unmatched ?? Array.Empty<UnmatchedFile>(),
                ModMetas = modMetas ?? new Dictionary<string, ModMeta>(),
            },
            Manifest = manifest,
            ManifestPath = manifestPath,
        };
    }

    [Fact]
    public void Build_MinimalResult_CopiesAllScalars()
    {
        var result = MakeResult();
        var summary = PackSummaryBuilder.Build(result);

        summary.Name.Should().Be("Test Pack");
        summary.Version.Should().Be("1.2.3");
        summary.Game.Should().Be("skyrimspecialedition");
        summary.InstancePath.Should().Be("/test/instance");
        summary.ModsTotal.Should().Be(0);
        summary.PluginsTotal.Should().Be(0);
        summary.LoadorderTotal.Should().Be(0);
        summary.ArchivesResolved.Should().Be(0);
        summary.ArchivesUnresolved.Should().Be(0);
        summary.ModsScanned.Should().Be(0);
        summary.FilesScanned.Should().Be(0);
        summary.DirectivesTotal.Should().Be(0);
        summary.DirectivesFromArchive.Should().Be(0);
        summary.UnmatchedFiles.Should().Be(0);
        summary.MetaIniCount.Should().Be(0);
        summary.ManifestMods.Should().Be(0);
        summary.ManifestArchives.Should().Be(0);
        summary.ManifestExtensions.Should().Be(0);
        summary.ManifestExtras.Should().Be(0);
        summary.UnmatchedWrittenTo.Should().BeNull();
        summary.ManifestPath.Should().Be("/test/__ModsyncManager_Output/modlist.json");
    }

    [Fact]
    public void Build_CountsDirectivesTotalAndFromArchive()
    {
        var fromArchive = new FromArchiveDirective
        {
            Archive = "a",
            Source = "s",
            Destination = "d",
            Hash = new XxHash64Value(0x1),
            Size = 1,
        };
        var createDir = new CreateDirectoryDirective
        {
            Destination = "meshes/empty",
        };

        var modDirectives = new Dictionary<string, IReadOnlyList<Directive>>(
            StringComparer.Ordinal)
        {
            ["ModA"] = new Directive[] { fromArchive, fromArchive, createDir },
            ["ModB"] = new Directive[] { fromArchive },
        };

        var result = MakeResult(modDirectives: modDirectives);
        var summary = PackSummaryBuilder.Build(result);

        summary.DirectivesTotal.Should().Be(4);
        summary.DirectivesFromArchive.Should().Be(3);
    }

    [Fact]
    public void Build_WithUnmatched_SetsUnmatchedWrittenTo()
    {
        var unmatched = new[]
        {
            new UnmatchedFile("Mod", "foo.txt", 10),
        };

        var result = MakeResult(unmatched: unmatched);
        var summary = PackSummaryBuilder.Build(result);

        summary.UnmatchedFiles.Should().Be(1);
        summary.UnmatchedWrittenTo.Should().Be(
            Path.Combine("/test/__ModsyncManager_Output", "mods"));
    }

    [Fact]
    public void Build_WithoutUnmatched_LeavesUnmatchedWrittenToNull()
    {
        var result = MakeResult();
        var summary = PackSummaryBuilder.Build(result);

        summary.UnmatchedFiles.Should().Be(0);
        summary.UnmatchedWrittenTo.Should().BeNull();
    }

    [Fact]
    public void Build_CopiesCountsFromSections()
    {
        var mods = new[]
        {
            new ModlistEntry("A", true),
            new ModlistEntry("B", false),
        };
        var plugins = new[] { new Mo2PluginEntry("x.esp", true) };
        var loadorder = new[] { "Skyrim.esm", "x.esp" };

        var resolved = new[]
        {
            new ArchiveEntry
            {
                Id = "a",
                Name = "a.7z",
                Size = 1,
                Hash = new XxHash64Value(0x1),
                Sources = Array.Empty<ArchiveSourceRef>(),
            },
        };

        var manifestMods = new[]
        {
            new ModEntry
            {
                Name = "A",
                Enabled = true,
                Order = 0,
                Directives = Array.Empty<Directive>(),
            },
        };
        var manifestArchives = new[]
        {
            new ArchiveEntry
            {
                Id = "a",
                Name = "a.7z",
                Size = 1,
                Hash = new XxHash64Value(0x1),
                Sources = Array.Empty<ArchiveSourceRef>(),
            },
        };
        var manifestExt = new[]
        {
            new ExtensionEntry
            {
                Name = "ext",
                Directives = Array.Empty<Directive>(),
            },
        };
        var manifestXst = new[]
        {
            new ExtensionEntry
            {
                Name = "xst",
                Directives = Array.Empty<Directive>(),
            },
        };

        var result = MakeResult(
            mods: mods,
            plugins: plugins,
            loadorder: loadorder,
            resolved: resolved,
            unresolvedCount: 2,
            manifestMods: manifestMods,
            manifestArchives: manifestArchives,
            manifestExtensions: manifestExt,
            manifestExtras: manifestXst);

        var summary = PackSummaryBuilder.Build(result);

        summary.ModsTotal.Should().Be(2);
        summary.PluginsTotal.Should().Be(1);
        summary.LoadorderTotal.Should().Be(2);
        summary.ArchivesResolved.Should().Be(1);
        summary.ArchivesUnresolved.Should().Be(2);
        summary.ManifestMods.Should().Be(1);
        summary.ManifestArchives.Should().Be(1);
        summary.ManifestExtensions.Should().Be(1);
        summary.ManifestExtras.Should().Be(1);
    }

    [Fact]
    public void Build_NullResult_Throws()
    {
        var act = () => PackSummaryBuilder.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
