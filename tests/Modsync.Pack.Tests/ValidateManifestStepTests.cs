// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class ValidateManifestStepTests
{
    private readonly ValidateManifestStep _step = new(
        NullLogger<ValidateManifestStep>.Instance);

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private static ArchiveEntry MakeArchive(string id, string name = "test.7z")
        => new()
        {
            Id = id,
            Name = name,
            Size = 1000,
            Hash = new XxHash64Value(0x1234),
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef { ModId = 1, FileId = 1, Game = "skyrimspecialedition" },
            },
        };

    private static ArchiveEntry MakeMo2Archive()
        => new()
        {
            Id = "local_mod-organizer-2-5-2",
            Name = "Mod.Organizer-2.5.2.7z",
            Size = 0,
            Hash = new XxHash64Value(0),
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef
                {
                    Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                    Hash = new XxHash64Value(0),
                },
            },
        };

    private static FromArchiveDirective MakeFromArchive(string archive, string path)
        => new()
        {
            Archive = archive,
            Source = path,
            Destination = path,
            Hash = new XxHash64Value(0xABCD),
            Size = 100,
        };

    /// <summary>
    /// Перегрузка с явными Source и Destination (для тестов
    /// валидации extension entries, где Name, Source и Destination
    /// — разные строки).
    /// </summary>
    private static FromArchiveDirective MakeFromArchive(
        string archive, string source, string destination)
        => new()
        {
            Archive = archive,
            Source = source,
            Destination = destination,
            Hash = new XxHash64Value(0xABCD),
            Size = 100,
        };

    private static ExtensionEntry MakeExtensionEntry(
        string name,
        params Directive[] directives) => new()
        {
            Name = name,
            Directives = directives,
        };

    private static ModEntry MakeMod(
        string name,
        bool enabled = true,
        int order = 0,
        IReadOnlyList<Directive>? directives = null) => new()
        {
            Name = name,
            Enabled = enabled,
            Order = order,
            Directives = directives ?? Array.Empty<Directive>(),
        };

    private static ModlistManifest MakeManifest(
        ArchiveEntry[]? archives = null,
        ModEntry[]? mods = null,
        PluginEntry[]? plugins = null,
        string[]? loadorder = null,
        ArchiveEntry? mo2Archive = null) => new()
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Execution = new ExecutionPolicy(),
            Mo2 = new Mo2Section
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = mo2Archive ?? MakeMo2Archive(),
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = archives ?? Array.Empty<ArchiveEntry>(),
            Mods = mods ?? Array.Empty<ModEntry>(),
            Plugins = plugins ?? Array.Empty<PluginEntry>(),
            Loadorder = loadorder ?? Array.Empty<string>(),
        };

    private static ModlistManifest MakeManifestWithExtensions(
        IReadOnlyList<ExtensionEntry> extensions) => new()
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Execution = new ExecutionPolicy(),
            Mo2 = new Mo2Section
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = MakeMo2Archive(),
                Extensions = extensions,
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

    private static ModlistManifest MakeManifestWithExtras(
        IReadOnlyList<ExtensionEntry> extras) => new()
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Execution = new ExecutionPolicy(),
            Mo2 = new Mo2Section
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = MakeMo2Archive(),
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection { Extras = extras },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MinimalValidManifest_Passes()
    {
        var manifest = MakeManifest();

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);

        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Execute_FullValidManifest_Passes()
    {
        var manifest = MakeManifest(
            archives: new[]
            {
                MakeArchive("nexus_skyrimspecialedition_1_1"),
                MakeArchive("local_somemod", "SomeMod.7z"),
            },
            mods: new[]
            {
                MakeMod("SkyUI", directives: new Directive[]
                {
                    MakeFromArchive("nexus_skyrimspecialedition_1_1", "interface/iconmenu.swf"),
                }),
                MakeMod("SomeMod", enabled: false, order: 1, directives: new Directive[]
                {
                    MakeFromArchive("local_somemod", "file.txt"),
                }),
            },
            plugins: new[]
            {
                new PluginEntry { Name = "Skyrim.esm", Enabled = true, Order = 0 },
                new PluginEntry { Name = "SkyUI.esp", Enabled = true, Order = 1 },
            },
            loadorder: new[] { "Skyrim.esm", "SkyUI.esp" });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);

        result.Should().BeSameAs(manifest);
    }

    // ------------------------------------------------------------------
    //  Дубликаты id / name
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DuplicateArchiveId_Throws()
    {
        var manifest = MakeManifest(archives: new[]
        {
            MakeArchive("nexus_skyrimspecialedition_1_1"),
            MakeArchive("nexus_skyrimspecialedition_1_1", "other.7z"),
        });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate archive id*");
    }

    [Fact]
    public async Task Execute_DuplicateModName_Throws()
    {
        var manifest = MakeManifest(mods: new[]
        {
            MakeMod("SkyUI"),
            MakeMod("SkyUI", order: 1),
        });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate mod name*");
    }

    [Fact]
    public async Task Execute_DuplicatePluginName_Throws()
    {
        var manifest = MakeManifest(plugins: new[]
        {
            new PluginEntry { Name = "SkyUI.esp", Enabled = true, Order = 0 },
            new PluginEntry { Name = "SkyUI.esp", Enabled = false, Order = 1 },
        });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate plugin name*");
    }

    [Fact]
    public async Task Execute_DuplicateLoadorderEntry_Throws()
    {
        var manifest = MakeManifest(loadorder: new[] { "Skyrim.esm", "Skyrim.esm" });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate plugin in loadorder*");
    }

    // ------------------------------------------------------------------
    //  Битая ссылка на архив
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FromArchiveRefersToMissingArchive_Throws()
    {
        var manifest = MakeManifest(
            archives: new[] { MakeArchive("nexus_skyrimspecialedition_1_1") },
            mods: new[]
            {
                MakeMod("SkyUI", directives: new Directive[]
                {
                    MakeFromArchive("nexus_skyrimspecialedition_999_999", "file.txt"),
                }),
            });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not exist in archives*");
    }

    [Fact]
    public async Task Execute_FromArchiveRefersToMo2Archive_Passes()
    {
        var mo2Archive = MakeMo2Archive();
        var manifest = MakeManifest(
            mo2Archive: mo2Archive,
            mods: new[]
            {
                MakeMod("SkyUI", directives: new Directive[]
                {
                    MakeFromArchive(mo2Archive.Id, "file.txt"),
                }),
            });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);

        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Execute_Mo2ArchiveIdCollidesWithArchives_Throws()
    {
        var mo2Archive = MakeMo2Archive();
        var manifest = MakeManifest(
            archives: new[] { MakeArchive(mo2Archive.Id, "collision.7z") },
            mo2Archive: mo2Archive);

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate archive id*");
    }

    // ------------------------------------------------------------------
    //  mo2.archive
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Mo2ArchiveEmptyId_Throws()
    {
        var manifest = MakeManifest(mo2Archive: new ArchiveEntry
        {
            Id = "",
            Name = "x.7z",
            Size = 0,
            Hash = new XxHash64Value(0),
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef
                {
                    Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                    Hash = new XxHash64Value(0),
                },
            },
        });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mo2.archive.id*");
    }

    [Fact]
    public async Task Execute_Mo2ArchiveEmptySources_Throws()
    {
        var manifest = MakeManifest(mo2Archive: new ArchiveEntry
        {
            Id = "mo2",
            Name = "x.7z",
            Size = 0,
            Hash = new XxHash64Value(0),
            Sources = Array.Empty<ArchiveSourceRef>(),
        });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mo2.archive.sources*");
    }

    // ------------------------------------------------------------------
    //  Множественные ошибки
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MultipleErrors_ReportsAll()
    {
        var manifest = MakeManifest(
            archives: new[]
            {
                MakeArchive("dup"),
                MakeArchive("dup", "other.7z"),
            },
            mods: new[]
            {
                MakeMod("SkyUI", directives: new Directive[]
                {
                    MakeFromArchive("missing-archive", "file.txt"),
                }),
            });

        var act = async () => await _step.ExecuteAsync(manifest, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("Duplicate archive id");
        ex.Which.Message.Should().Contain("does not exist in archives");
    }

    // ------------------------------------------------------------------
    //  ValidateExtensionEntries: mo2.extensions[]
    // ------------------------------------------------------------------

    [Fact]
    public async Task Extensions_FileEntry_DestinationEqualsName_Passes()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("plugins/fomod.dll",
                MakeFromArchive("local_ext", "fomod.dll", "plugins/fomod.dll")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_FolderEntry_AllDestinationsUnderPrefix_Passes()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI",
                MakeFromArchive("local_pkg", "BethINI.exe",
                    "tools/BethINI/BethINI.exe"),
                MakeFromArchive("local_pkg", "readme.txt",
                    "tools/BethINI/readme.txt")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_FolderEntry_DeepDestinationsUnderPrefix_Passes()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI",
                MakeFromArchive("local_pkg", "sub/deep.bin",
                    "tools/BethINI/sub/deep.bin")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_FolderEntry_DestinationOutside_Fails()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI",
                MakeFromArchive("local_pkg", "BethINI.exe",
                    "tools/BethINI/BethINI.exe"),
                MakeFromArchive("local_pkg", "other.dll",
                    "plugins/other.dll")),  // ← вне entry
        });

        var act = async () => await _step.ExecuteAsync(
            manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*plugins/other.dll*")
            .WithMessage("*tools/BethINI*");
    }

    [Fact]
    public async Task Extensions_FileEntry_DestinationIsSibling_Fails()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("plugins/fomod.dll",
                MakeFromArchive("local_ext", "other.dll",
                    "plugins/other.dll")),  // ← не совпадает с Name
        });

        var act = async () => await _step.ExecuteAsync(
            manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*plugins/other.dll*");
    }

    [Fact]
    public async Task Extensions_FileEntry_DestinationIsParentFolder_Fails()
    {
        // Destination — родитель entry (не совпадает, не начинается с prefix).
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("plugins/fomod.dll",
                MakeFromArchive("local_ext", "fomod.dll",
                    "plugins")),  // ← не под entry
        });

        var act = async () => await _step.ExecuteAsync(
            manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Extensions_BackslashInName_Normalized_Passes()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry(@"tools\BethINI",
                MakeFromArchive("local_pkg", "BethINI.exe",
                    "tools/BethINI/BethINI.exe")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_TrailingSlashInName_Normalized_Passes()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI/",
                MakeFromArchive("local_pkg", "BethINI.exe",
                    "tools/BethINI/BethINI.exe")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_BackslashInDestination_Normalized_Passes()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI",
                MakeFromArchive("local_pkg", "BethINI.exe",
                    @"tools\BethINI\BethINI.exe")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_CaseDifference_MatchesIgnoringCase()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI",
                MakeFromArchive("local_pkg", "BethINI.exe",
                    "Tools/Bethini/BethINI.exe")),  // другой регистр
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_EmptyDirectives_NotValidated()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/Empty"),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_OnlyNonFromArchiveDirectives_NotValidated()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/Weird",
                new CreateDirectoryDirective { Destination = "anywhere/" }),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extensions_EmptyName_Fails()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("",
                MakeFromArchive("local_ext", "fomod.dll", "plugins/fomod.dll")),
        });

        var act = async () => await _step.ExecuteAsync(
            manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*mo2.extensions[0].name must be non-empty*");
    }

    // ------------------------------------------------------------------
    //  ValidateExtensionEntries: stockGame.extras[]
    // ------------------------------------------------------------------

    [Fact]
    public async Task Extras_FileEntry_DestinationEqualsName_Passes()
    {
        var manifest = MakeManifestWithExtras(new[]
        {
            MakeExtensionEntry("skse64_loader.exe",
                MakeFromArchive("local_skse", "skse64_loader.exe",
                    "skse64_loader.exe")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extras_FolderEntry_AllDestinationsUnderPrefix_Passes()
    {
        var manifest = MakeManifestWithExtras(new[]
        {
            MakeExtensionEntry("enbseries",
                MakeFromArchive("local_enb", "enbseries.ini",
                    "enbseries/enbseries.ini"),
                MakeFromArchive("local_enb", "enblocal.ini",
                    "enbseries/enblocal.ini")),
        });

        var result = await _step.ExecuteAsync(manifest, CancellationToken.None);
        result.Should().BeSameAs(manifest);
    }

    [Fact]
    public async Task Extras_DestinationOutside_Fails()
    {
        var manifest = MakeManifestWithExtras(new[]
        {
            MakeExtensionEntry("enbseries",
                MakeFromArchive("local_enb", "enbseries.ini",
                    "enbseries/enbseries.ini"),
                MakeFromArchive("local_enb", "sibling.dll",
                    "sibling.dll")),  // ← вне entry
        });

        var act = async () => await _step.ExecuteAsync(
            manifest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*stockGame.extras[0]*")
            .WithMessage("*sibling.dll*");
    }

    // ------------------------------------------------------------------
    //  Множественные ошибки в extension entries
    // ------------------------------------------------------------------

    [Fact]
    public async Task Extensions_MultipleEntries_MultipleErrors()
    {
        var manifest = MakeManifestWithExtensions(new[]
        {
            MakeExtensionEntry("tools/BethINI",
                MakeFromArchive("local_pkg", "x.dll", "plugins/x.dll")),
            MakeExtensionEntry("enbseries",
                MakeFromArchive("local_enb", "y.dll", "other/y.dll")),
        });

        var act = async () => await _step.ExecuteAsync(
            manifest, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("plugins/x.dll");
        ex.Which.Message.Should().Contain("other/y.dll");
    }
}
