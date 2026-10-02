// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Mo2;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

using Mo2PluginEntry = Modsync.Core.Models.Mo2.PluginEntry;

namespace Modsync.Pack.Tests;

public class BuildManifestStepTests
{
    private const string Mo2ArchiveName = "Mod.Organizer-2.5.2.7z";
    private static readonly XxHash64Value Mo2Hash = new(0xE574E05EB6C470AD);
    private const long Mo2Size = 149_660_212L;

    private readonly BuildManifestStep _step = new(
        NullLogger<BuildManifestStep>.Instance);

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private static PackConfig MakeConfig(
        string mo2Archive = Mo2ArchiveName,
        ArchiveSourceRef? mo2Source = null,
        string profile = "Default") => new()
        {
            Meta = new PackMeta
            {
                Name = "Test Pack",
                Version = "1.2.3",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Instance = new PackInstance { Path = "Test Pack" },
            Mo2 = new PackMo2
            {
                Version = "2.5.2",
                Profile = profile,
                Archive = mo2Archive,
                Source = mo2Source ?? new MirrorSourceRef
                {
                    Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                    Hash = Mo2Hash,
                },
                Extensions = Array.Empty<string>(),
            },
            StockGame = new PackStockGame { Extras = Array.Empty<string>() },
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

    private static InstanceSnapshot MakeSnapshot(
        ModlistEntry[]? mods = null,
        Mo2PluginEntry[]? plugins = null,
        string[]? loadorder = null) => new()
        {
            InstancePath = "/test",
            Mo2Path = "/test/MO2",
            DownloadsPath = "/test/MO2/downloads",
            ModsPath = "/test/MO2/mods",
            ProfilesPath = "/test/MO2/profiles",
            StockGamePath = "/test/Stock Game",
            ModsyncOutputPath = "/test/__ModsyncManager_Output",
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

    /// <summary>
    /// ArchiveIndex с MO2-архивом в Resolved.
    /// </summary>
    private static ArchiveIndex ArchiveIndexWithMo2Resolved(
        string name = Mo2ArchiveName,
        long size = Mo2Size,
        XxHash64Value? hash = null) => new()
        {
            Resolved = new[]
            {
                new ArchiveEntry
                {
                    Id = "local_mod-organizer-2-5-2",
                    Name = name,
                    Size = size,
                    Hash = hash ?? Mo2Hash,
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                            Hash = Mo2Hash,
                        },
                    },
                },
            },
            Unresolved = Array.Empty<UnresolvedArchive>(),
        };

    /// <summary>
    /// ArchiveIndex с MO2-архивом в Unresolved.
    /// </summary>
    private static ArchiveIndex ArchiveIndexWithMo2Unresolved(
        string name = Mo2ArchiveName,
        long size = Mo2Size,
        XxHash64Value? hash = null) => new()
        {
            Resolved = Array.Empty<ArchiveEntry>(),
            Unresolved = new[]
            {
                new UnresolvedArchive
                {
                    FullPath = "/test/downloads/" + name,
                    FileName = name,
                    Size = size,
                    Hash = hash ?? Mo2Hash,
                },
            },
        };

    private static ArchiveIndex EmptyArchiveIndex() => new()
    {
        Resolved = Array.Empty<ArchiveEntry>(),
        Unresolved = Array.Empty<UnresolvedArchive>(),
    };

    private static ModScanResult EmptyModScan() => new()
    {
        Mods = new Dictionary<string, IReadOnlyList<ScannedFile>>(),
    };

    private static MatchResult EmptyMatch() => new()
    {
        ModDirectives = new Dictionary<string, IReadOnlyList<Directive>>(),
        Unmatched = Array.Empty<UnmatchedFile>(),
        ModMetas = new Dictionary<string, ModMeta>(),
    };

    private static BuildManifestStep.Input MakeInput(
        PackConfig? config = null,
        InstanceSnapshot? snapshot = null,
        ArchiveIndex? archiveIndex = null,
        ModScanResult? modScan = null,
        MatchResult? match = null) => new()
        {
            Config = config ?? MakeConfig(),
            Snapshot = snapshot ?? MakeSnapshot(),
            ArchiveIndex = archiveIndex ?? ArchiveIndexWithMo2Resolved(),
            ModScan = modScan ?? EmptyModScan(),
            Match = match ?? EmptyMatch(),
            ExtensionsScan = EmptyEntryScan(),
            ExtrasScan = EmptyEntryScan(),
            ExtensionsMatch = EmptyMatchEntries(),
            ExtrasMatch = EmptyMatchEntries(),
        };

    private static EntryScanResult EmptyEntryScan() => new()
    {
        Entries = new Dictionary<string, IReadOnlyList<ScannedFile>>(
            StringComparer.Ordinal),
    };

    private static MatchEntriesResult EmptyMatchEntries() => new()
    {
        Directives = new Dictionary<string, IReadOnlyList<Directive>>(
            StringComparer.Ordinal),
        Unmatched = Array.Empty<UnmatchedEntry>(),
    };

    private static FromArchiveDirective MakeDirective(string archive, string path) => new()
    {
        Archive = archive,
        Source = path,
        Destination = path,
        Hash = new XxHash64Value(0xABCD),
        Size = 100,
    };

    // ------------------------------------------------------------------
    //  Заголовок и meta
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyInput_ProducesMinimalManifest()
    {
        var manifest = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        manifest.SchemaVersion.Should().Be("1.0.0");
        manifest.ManifestVersion.Should().Be("1.2.3");
        manifest.CreatedBy.Should().Be("modsyncmanager-pack/0.1.0");
        manifest.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        manifest.Meta.Name.Should().Be("Test Pack");
        manifest.Meta.Version.Should().Be("1.2.3");
        manifest.Meta.Author.Should().Be("tester");
        manifest.Meta.Game.Should().Be("skyrimspecialedition");
        manifest.Meta.GameVersion.Should().Be("1.6.1170");

        manifest.Execution.Directives.Should().Be("sequential");
        manifest.Execution.OnConflict.Should().Be("lastWins");

        manifest.Mods.Should().BeEmpty();
        manifest.Plugins.Should().BeEmpty();
        manifest.Loadorder.Should().BeEmpty();
        manifest.Archives.Should().BeEmpty();

        manifest.Mo2.Version.Should().Be("2.5.2");
        manifest.Mo2.Profile.Should().Be("Default");
        manifest.Mo2.Extensions.Should().BeEmpty();
        manifest.StockGame.Extras.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_CustomProfile_PropagatedToManifest()
    {
        var config = MakeConfig(profile: "NordicUI");
        var manifest = await _step.ExecuteAsync(
            MakeInput(config: config), CancellationToken.None);

        manifest.Mo2.Profile.Should().Be("NordicUI");
    }

    // ------------------------------------------------------------------
    //  Mods
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EnabledModWithDirectives_ProducesModEntry()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("SkyUI", true),
        });

        var match = new MatchResult
        {
            ModDirectives = new Dictionary<string, IReadOnlyList<Directive>>
            {
                ["SkyUI"] = new Directive[]
                {
                    MakeDirective("nexus_skyrimspecialedition_3863_1000", "interface/iconmenu.swf"),
                },
            },
            Unmatched = Array.Empty<UnmatchedFile>(),
            ModMetas = new Dictionary<string, ModMeta>(),
        };

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot, match: match), CancellationToken.None);

        manifest.Mods.Should().HaveCount(1);
        var mod = manifest.Mods[0];
        mod.Name.Should().Be("SkyUI");
        mod.Enabled.Should().BeTrue();
        mod.Order.Should().Be(0);
        mod.Meta.Should().BeNull();
        mod.Directives.Should().HaveCount(1);
        mod.Directives[0].Should().BeOfType<FromArchiveDirective>();
    }

    [Fact]
    public async Task Execute_DisabledMod_ProducesModEntryWithEnabledFalse()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("Requiem", false),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Mods.Should().HaveCount(1);
        manifest.Mods[0].Name.Should().Be("Requiem");
        manifest.Mods[0].Enabled.Should().BeFalse();
        manifest.Mods[0].Order.Should().Be(0);
    }

    [Fact]
    public async Task Execute_MixedMods_OrderIsIndexInModlist()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("First", true),
            new ModlistEntry("Second", false),
            new ModlistEntry("Third", true),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Mods.Should().HaveCount(3);
        manifest.Mods[0].Name.Should().Be("First");
        manifest.Mods[0].Order.Should().Be(0);
        manifest.Mods[1].Name.Should().Be("Second");
        manifest.Mods[1].Order.Should().Be(1);
        manifest.Mods[2].Name.Should().Be("Third");
        manifest.Mods[2].Order.Should().Be(2);
    }

    [Fact]
    public async Task Execute_NoDeleteMod_ExcludedFromManifest()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("Normal", true),
            new ModlistEntry("[NoDelete]MyMod", true),
            new ModlistEntry("Another", true),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Mods.Should().HaveCount(2);
        manifest.Mods.Should().NotContain(m => m.Name == "[NoDelete]MyMod");

        manifest.Mods[0].Name.Should().Be("Normal");
        manifest.Mods[0].Order.Should().Be(0);
        manifest.Mods[1].Name.Should().Be("Another");
        manifest.Mods[1].Order.Should().Be(2);
    }

    [Fact]
    public async Task Execute_SeparatorMod_IncludedInManifest()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("Normal", true),
            new ModlistEntry("# \U0001F4C2 Мои моды_separator", false),
            new ModlistEntry("Another", true),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Mods.Should().HaveCount(3);
        manifest.Mods[1].Name.Should().Be("# \U0001F4C2 Мои моды_separator");
        manifest.Mods[1].Enabled.Should().BeFalse();
        manifest.Mods[1].Order.Should().Be(1);
    }

    [Fact]
    public async Task Execute_ModWithMeta_IncludesModMeta()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("Actor Limit Fix", true),
        });

        var modMeta = new ModMeta
        {
            GameName = "Skyrim Special Edition",
            GameId = "skyrimspecialedition",
            ModId = 32349,
            FileId = 795423,
            Version = "1.7.0",
            Repository = "Nexus",
            Url = "https://www.nexusmods.com/skyrimspecialedition/mods/32349",
            Comments = "",
            Notes = "my note",
        };

        var match = new MatchResult
        {
            ModDirectives = new Dictionary<string, IReadOnlyList<Directive>>(),
            Unmatched = Array.Empty<UnmatchedFile>(),
            ModMetas = new Dictionary<string, ModMeta>
            {
                ["Actor Limit Fix"] = modMeta,
            },
        };

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot, match: match), CancellationToken.None);

        manifest.Mods.Should().HaveCount(1);
        manifest.Mods[0].Meta.Should().NotBeNull();
        manifest.Mods[0].Meta!.ModId.Should().Be(32349);
        manifest.Mods[0].Meta!.FileId.Should().Be(795423);
        manifest.Mods[0].Meta!.Version.Should().Be("1.7.0");
        manifest.Mods[0].Meta!.Notes.Should().Be("my note");
    }

    [Fact]
    public async Task Execute_ModWithoutMeta_HasNullMeta()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("NoMetaMod", true),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Mods.Should().HaveCount(1);
        manifest.Mods[0].Meta.Should().BeNull();
    }

    [Fact]
    public async Task Execute_ModMissingFromScan_HasEmptyDirectivesAndNullMeta()
    {
        var snapshot = MakeSnapshot(mods: new[]
        {
            new ModlistEntry("MissingMod", false),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Mods.Should().HaveCount(1);
        manifest.Mods[0].Name.Should().Be("MissingMod");
        manifest.Mods[0].Enabled.Should().BeFalse();
        manifest.Mods[0].Directives.Should().BeEmpty();
        manifest.Mods[0].Meta.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Plugins и Loadorder
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Plugins_AllIncludedWithOrder()
    {
        var snapshot = MakeSnapshot(plugins: new[]
        {
            new Mo2PluginEntry("Skyrim.esm", true),
            new Mo2PluginEntry("Disabled.esp", false),
            new Mo2PluginEntry("Update.esm", true),
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Plugins.Should().HaveCount(3);
        manifest.Plugins[0].Name.Should().Be("Skyrim.esm");
        manifest.Plugins[0].Enabled.Should().BeTrue();
        manifest.Plugins[0].Order.Should().Be(0);
        manifest.Plugins[1].Name.Should().Be("Disabled.esp");
        manifest.Plugins[1].Enabled.Should().BeFalse();
        manifest.Plugins[1].Order.Should().Be(1);
        manifest.Plugins[2].Name.Should().Be("Update.esm");
        manifest.Plugins[2].Order.Should().Be(2);
    }

    [Fact]
    public async Task Execute_Loadorder_CopiedVerbatim()
    {
        var snapshot = MakeSnapshot(loadorder: new[]
        {
            "Skyrim.esm",
            "Update.esm",
            "SkyUI.esp",
        });

        var manifest = await _step.ExecuteAsync(
            MakeInput(snapshot: snapshot), CancellationToken.None);

        manifest.Loadorder.Should().Equal("Skyrim.esm", "Update.esm", "SkyUI.esp");
    }

    // ------------------------------------------------------------------
    //  Archives
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Archives_CopiedFromArchiveIndex()
    {
        var entry = new ArchiveEntry
        {
            Id = "nexus_skyrimspecialedition_1_1",
            Name = "SkyUI.7z",
            Size = 1000,
            Hash = new XxHash64Value(0x1234),
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef { ModId = 1, FileId = 1, Game = "skyrimspecialedition" },
            },
        };
        var archiveIndex = new ArchiveIndex
        {
            Resolved = new[] { entry },
            Unresolved = Array.Empty<UnresolvedArchive>(),
        };

        // В этом тесте ArchiveIndex содержит SkyUI, но не MO2-архив.
        // Значит, BuildMo2Archive пойдёт в ветку «не найден» и возьмёт hash
        // из source. Это ок — тест проверяет только Archives.
        var manifest = await _step.ExecuteAsync(
            MakeInput(archiveIndex: archiveIndex), CancellationToken.None);

        manifest.Archives.Should().HaveCount(1);
        manifest.Archives[0].Id.Should().Be("nexus_skyrimspecialedition_1_1");
        manifest.Archives[0].Name.Should().Be("SkyUI.7z");
    }

    // ------------------------------------------------------------------
    //  MO2 archive
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Mo2ArchiveInDownloadsResolved_TakesSizeFromFile_HashFromSource()
    {
        var archiveIndex = ArchiveIndexWithMo2Resolved(size: 123_456);
        var manifest = await _step.ExecuteAsync(
            MakeInput(archiveIndex: archiveIndex), CancellationToken.None);

        manifest.Mo2.Archive.Name.Should().Be(Mo2ArchiveName);
        manifest.Mo2.Archive.Size.Should().Be(123_456);
        manifest.Mo2.Archive.Hash.Should().Be(Mo2Hash);
        manifest.Mo2.Archive.Id.Should().Be("local_mod-organizer-2-5-2");
        manifest.Mo2.Archive.Sources.Should().HaveCount(1);
        manifest.Mo2.Archive.Sources[0].Should().BeOfType<MirrorSourceRef>();
    }

    [Fact]
    public async Task Execute_Mo2ArchiveInDownloadsResolved_HashMismatch_Throws()
    {
        var wrongHash = new XxHash64Value(0xBADBADBADBADBAD0);
        var archiveIndex = ArchiveIndexWithMo2Resolved(hash: wrongHash);

        var act = async () => await _step.ExecuteAsync(
            MakeInput(archiveIndex: archiveIndex), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*hash mismatch*");
    }

    [Fact]
    public async Task Execute_Mo2ArchiveInDownloadsUnresolved_TakesSizeFromFile_HashFromSource()
    {
        var archiveIndex = ArchiveIndexWithMo2Unresolved(size: 999_000);
        var manifest = await _step.ExecuteAsync(
            MakeInput(archiveIndex: archiveIndex), CancellationToken.None);

        manifest.Mo2.Archive.Name.Should().Be(Mo2ArchiveName);
        manifest.Mo2.Archive.Size.Should().Be(999_000);
        manifest.Mo2.Archive.Hash.Should().Be(Mo2Hash);
        manifest.Mo2.Archive.Id.Should().Be("local_mod-organizer-2-5-2");
    }

    [Fact]
    public async Task Execute_Mo2ArchiveInDownloadsUnresolved_HashMismatch_Throws()
    {
        var wrongHash = new XxHash64Value(0xBADBADBADBADBAD0);
        var archiveIndex = ArchiveIndexWithMo2Unresolved(hash: wrongHash);

        var act = async () => await _step.ExecuteAsync(
            MakeInput(archiveIndex: archiveIndex), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*hash mismatch*");
    }

    [Fact]
    public async Task Execute_Mo2ArchiveMissing_SizeZero_HashFromSource()
    {
        var manifest = await _step.ExecuteAsync(
            MakeInput(archiveIndex: EmptyArchiveIndex()), CancellationToken.None);

        manifest.Mo2.Archive.Name.Should().Be(Mo2ArchiveName);
        manifest.Mo2.Archive.Size.Should().Be(0);
        manifest.Mo2.Archive.Hash.Should().Be(Mo2Hash);
        manifest.Mo2.Archive.Id.Should().Be("local_mod-organizer-2-5-2");
        manifest.Mo2.Archive.Sources.Should().HaveCount(1);
        manifest.Mo2.Archive.Sources[0].Should().BeOfType<MirrorSourceRef>();
    }

    [Fact]
    public async Task Execute_Mo2SourceNotMirror_Throws()
    {
        var config = MakeConfig(mo2Source: new NexusSourceRef
        {
            ModId = 1,
            FileId = 1,
            Game = "skyrimspecialedition",
        });

        var act = async () => await _step.ExecuteAsync(
            MakeInput(config: config), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mirror*");
    }

    [Fact]
    public async Task Execute_NonEmptyExtensionsMatch_PropagatesToManifest()
    {
        var scan = new EntryScanResult
        {
            Entries = new Dictionary<string, IReadOnlyList<ScannedFile>>(
                StringComparer.Ordinal)
            {
                ["plugins/fomod.dll"] = new[]
                {
                    new ScannedFile
                    {
                        RelativePath = "plugins/fomod.dll",
                        Hash = new XxHash64Value(0x1),
                        Size = 10,
                    },
                },
            },
        };

        var directives = new Dictionary<string, IReadOnlyList<Directive>>(
            StringComparer.Ordinal)
        {
            ["plugins/fomod.dll"] = new Directive[]
            {
                MakeDirective("local_mo2", "plugins/fomod.dll"),
            },
        };

        var matchEntries = new MatchEntriesResult
        {
            Directives = directives,
            Unmatched = Array.Empty<UnmatchedEntry>(),
        };

        var input = MakeInput() with
        {
            ExtensionsScan = scan,
            ExtensionsMatch = matchEntries,
        };

        var manifest = await _step.ExecuteAsync(input, CancellationToken.None);

        manifest.Mo2.Extensions.Should().HaveCount(1);
        manifest.Mo2.Extensions[0].Name.Should().Be("plugins/fomod.dll");
        manifest.Mo2.Extensions[0].Directives.Should().HaveCount(1);
    }

    [Fact]
    public async Task Execute_NonEmptyExtrasMatch_PropagatesToManifest()
    {
        var scan = new EntryScanResult
        {
            Entries = new Dictionary<string, IReadOnlyList<ScannedFile>>(
                StringComparer.Ordinal)
            {
                ["enbseries"] = new[]
                {
                    new ScannedFile
                    {
                        RelativePath = "enbseries/enb.ini",
                        Hash = new XxHash64Value(0x2),
                        Size = 20,
                    },
                },
            },
        };

        var directives = new Dictionary<string, IReadOnlyList<Directive>>(
            StringComparer.Ordinal)
        {
            ["enbseries"] = new Directive[]
            {
                MakeDirective("local_enb", "enbseries/enb.ini"),
            },
        };

        var matchEntries = new MatchEntriesResult
        {
            Directives = directives,
            Unmatched = Array.Empty<UnmatchedEntry>(),
        };

        var input = MakeInput() with
        {
            ExtrasScan = scan,
            ExtrasMatch = matchEntries,
        };

        var manifest = await _step.ExecuteAsync(input, CancellationToken.None);

        manifest.StockGame.Extras.Should().HaveCount(1);
        manifest.StockGame.Extras[0].Name.Should().Be("enbseries");
        manifest.StockGame.Extras[0].Directives.Should().HaveCount(1);
    }
}
