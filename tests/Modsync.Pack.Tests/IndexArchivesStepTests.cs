// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class IndexArchivesStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileHashCache _cache = new();
    private readonly IndexArchivesStep _step;

    public IndexArchivesStepTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "modsyncmanager-idx-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _step = new IndexArchivesStep(_cache, NullLogger<IndexArchivesStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static PackConfig EmptyConfig() => new()
    {
        Meta = new PackMeta
        {
            Name = "Test",
            Version = "1.0.0",
            Author = "a",
            Game = "skyrimspecialedition",
            GameVersion = "1.6.1170",
        },
        Instance = new PackInstance { Path = "Test" },
        Mo2 = new PackMo2
        {
            Version = "2.5.2",
            Profile = "Default",
            Archive = "x.7z",
            Source = new MirrorSourceRef
            {
                Url = "https://example.com/x.7z",
                Hash = new XxHash64Value(0xabc),
            },
            Extensions = Array.Empty<string>(),
        },
        StockGame = new PackStockGame { Extras = Array.Empty<string>() },
        ArchiveSources = Array.Empty<PackArchiveSource>(),
    };

    private IndexArchivesStep.Input MakeInput(PackConfig? config = null) => new()
    {
        DownloadsPath = _tempDir,
        Config = config ?? EmptyConfig(),
        GameDomain = "skyrimspecialedition",
        ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
    };

    [Fact]
    public async Task Execute_EmptyDirectory_ReturnsEmptyIndex()
    {
        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().BeEmpty();
        result.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_IgnoresNonArchiveFiles()
    {
        WriteFile("readme.txt", "hello");
        WriteFile("notes.md", "# notes");
        WriteFile("archive.meta", "[General]\nmodID=1\nfileID=2\ngameName=Skyrim");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().BeEmpty();
        result.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WithValidMeta_ResolvesAsNexus()
    {
        WriteFile("SkyUI.7z", "fake archive content");
        WriteFile("SkyUI.7z.meta",
            "[General]\ngameName=Skyrim\nmodID=3863\nfileID=1000172397");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().HaveCount(1);
        var entry = result.Resolved[0];
        entry.Id.Should().Be("nexus_skyrimspecialedition_3863_1000172397");
        entry.Name.Should().Be("SkyUI.7z");
        entry.Sources.Should().HaveCount(1);
        entry.Sources[0].Should().BeOfType<NexusSourceRef>();
        var nexus = (NexusSourceRef)entry.Sources[0];
        nexus.ModId.Should().Be(3863);
        nexus.FileId.Should().Be(1000172397);
        nexus.Game.Should().Be("skyrimspecialedition");
    }

    [Fact]
    public async Task Execute_WithoutMetaButInArchiveSources_ResolvesAsLocal()
    {
        WriteFile("SomeMod.7z", "fake content");

        var config = EmptyConfig() with
        {
            ArchiveSources = new[]
            {
                new PackArchiveSource
                {
                    Archive = "SomeMod.7z",
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/SomeMod.7z",
                            Hash = new Modsync.Core.Models.Hashing.XxHash64Value(0xabc),
                        },
                    },
                },
            },
        };

        var result = await _step.ExecuteAsync(MakeInput(config), CancellationToken.None);

        result.Resolved.Should().HaveCount(1);
        result.Resolved[0].Id.Should().Be("local_somemod");
        result.Resolved[0].Sources[0].Should().BeOfType<MirrorSourceRef>();
    }

    [Fact]
    public async Task Execute_WithoutMetaAndWithoutSource_BecomesUnresolved()
    {
        WriteFile("MysteryMod.7z", "fake content");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().BeEmpty();
        result.Unresolved.Should().HaveCount(1);
        result.Unresolved[0].FileName.Should().Be("MysteryMod.7z");
    }

    [Fact]
    public async Task Execute_InvalidMetaFile_FallsBackToUnresolved()
    {
        WriteFile("Bad.7z", "fake");
        WriteFile("Bad.7z.meta", "[General]\ndirectURL=https://example.com");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().BeEmpty();
        result.Unresolved.Should().HaveCount(1);
        result.Unresolved[0].FileName.Should().Be("Bad.7z");
    }

    [Fact]
    public async Task Execute_InvalidMetaFile_ButInArchiveSources_ResolvesAsLocal()
    {
        WriteFile("Bad.7z", "fake");
        WriteFile("Bad.7z.meta", "[General]\ndirectURL=https://example.com");

        var config = EmptyConfig() with
        {
            ArchiveSources = new[]
            {
            new PackArchiveSource
            {
                Archive = "Bad.7z",
                Sources = new ArchiveSourceRef[]
                {
                    new MirrorSourceRef
                    {
                        Url = "https://example.com/Bad.7z",
                        Hash = new Modsync.Core.Models.Hashing.XxHash64Value(0xabc),
                    },
                },
            },
        },
        };

        var result = await _step.ExecuteAsync(MakeInput(config), CancellationToken.None);

        result.Resolved.Should().HaveCount(1);
        result.Resolved[0].Id.Should().Be("local_bad");
        result.Resolved[0].Sources[0].Should().BeOfType<MirrorSourceRef>();
    }

    [Fact]
    public async Task Execute_DuplicateNexusIds_Throws()
    {
        WriteFile("Version1.7z", "v1");
        WriteFile("Version1.7z.meta",
            "[General]\ngameName=Skyrim\nmodID=100\nfileID=200");
        WriteFile("Version2.7z", "v2");
        WriteFile("Version2.7z.meta",
            "[General]\ngameName=Skyrim\nmodID=100\nfileID=200");

        var act = async () => await _step.ExecuteAsync(MakeInput(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate*");
    }

    [Fact]
    public async Task Execute_HashIsStableAcrossRuns()
    {
        WriteFile("SkyUI.7z", "content");
        WriteFile("SkyUI.7z.meta",
            "[General]\ngameName=Skyrim\nmodID=1\nfileID=2");

        var r1 = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);
        var r2 = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        r1.Resolved[0].Hash.Should().Be(r2.Resolved[0].Hash);
        _cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task Execute_MultipleArchives_SortedById()
    {
        WriteFile("B.7z", "b");
        WriteFile("B.7z.meta", "[General]\ngameName=Skyrim\nmodID=2\nfileID=2");
        WriteFile("A.7z", "a");
        WriteFile("A.7z.meta", "[General]\ngameName=Skyrim\nmodID=1\nfileID=1");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().HaveCount(2);
        result.Resolved[0].Id.Should().Be("nexus_skyrimspecialedition_1_1");
        result.Resolved[1].Id.Should().Be("nexus_skyrimspecialedition_2_2");
    }

    [Fact]
    public async Task Execute_WithValidMeta_PopulatesArchiveEntryMeta()
    {
        WriteFile("SkyUI.7z", "fake archive content");
        WriteFile("SkyUI.7z.meta",
            "[General]\r\n" +
            "gameName=Skyrim Special Edition\r\n" +
            "gameID=skyrimspecialedition\r\n" +
            "modID=3863\r\n" +
            "fileID=1000172397\r\n" +
            "version=5.1\r\n" +
            "repository=Nexus\r\n" +
            "url=https://www.nexusmods.com/skyrimspecialedition/mods/3863\r\n" +
            "notes=test note\r\n");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        result.Resolved.Should().HaveCount(1);
        var entry = result.Resolved[0];
        entry.Meta.Should().NotBeNull();
        entry.Meta!.GameName.Should().Be("Skyrim Special Edition");
        entry.Meta!.GameId.Should().Be("skyrimspecialedition");
        entry.Meta!.ModId.Should().Be(3863);
        entry.Meta!.FileId.Should().Be(1000172397);
        entry.Meta!.Version.Should().Be("5.1");
        entry.Meta!.Repository.Should().Be("Nexus");
        entry.Meta!.Url.Should().Be("https://www.nexusmods.com/skyrimspecialedition/mods/3863");
        entry.Meta!.Notes.Should().Be("test note");
    }

    [Fact]
    public async Task Execute_WithoutMeta_MetaIsNull()
    {
        WriteFile("SomeMod.7z", "fake content");

        var config = EmptyConfig() with
        {
            ArchiveSources = new[]
            {
                new PackArchiveSource
                {
                    Archive = "SomeMod.7z",
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/SomeMod.7z",
                            Hash = new Modsync.Core.Models.Hashing.XxHash64Value(0xabc),
                        },
                    },
                },
            },
        };

        var result = await _step.ExecuteAsync(
            MakeInput(config), CancellationToken.None);

        result.Resolved.Should().HaveCount(1);
        result.Resolved[0].Meta.Should().BeNull();
    }

    [Fact]
    public async Task Execute_WithInvalidMeta_FallsBackToUnresolved_MetaNotSet()
    {
        WriteFile("Bad.7z", "fake");
        WriteFile("Bad.7z.meta",
            "[General]\r\ndirectURL=https://example.com/x.7z\r\n");

        var result = await _step.ExecuteAsync(MakeInput(), CancellationToken.None);

        // Невалидный .meta (нет modID/fileID) → Unresolved.
        result.Resolved.Should().BeEmpty();
        result.Unresolved.Should().HaveCount(1);
        result.Unresolved[0].FileName.Should().Be("Bad.7z");
    }
}
