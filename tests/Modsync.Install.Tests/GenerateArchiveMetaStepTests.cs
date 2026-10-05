// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install.Steps;
using Modsync.Platform.MO2.Readers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class GenerateArchiveMetaStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _downloadsDir;
    private readonly GenerateArchiveMetaStep _step;

    public GenerateArchiveMetaStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-archive-meta-" + Guid.NewGuid());
        _downloadsDir = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(_downloadsDir);

        _step = new GenerateArchiveMetaStep(
            NullLogger<GenerateArchiveMetaStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static ArchiveEntry MakeArchive(
        string id,
        string name,
        ModMeta? meta = null) => new()
        {
            Id = id,
            Name = name,
            Size = 1000,
            Hash = new XxHash64Value(0x1234),
            Sources = new ArchiveSourceRef[]
        {
            new NexusSourceRef
            {
                ModId = 1,
                FileId = 1,
                Game = "skyrimspecialedition",
            },
        },
            Meta = meta,
        };

    private static ArchiveEntry MakeMo2Archive(ModMeta? meta = null) => new()
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
        Meta = meta,
    };

    private static ModlistManifest MakeManifest(
        IReadOnlyList<ArchiveEntry>? archives = null,
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
                Author = "t",
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
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

    private GenerateArchiveMetaStep.Input MakeInput(ModlistManifest manifest)
        => new()
        {
            Manifest = manifest,
            DownloadsPath = _downloadsDir,
        };

    private string MetaPath(string archiveName)
        => Path.Combine(_downloadsDir, archiveName + ".meta");

    // ------------------------------------------------------------------
    //  Basic
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveWithMeta_WritesMetaFile()
    {
        var meta = new ModMeta
        {
            GameName = "Skyrim Special Edition",
            GameId = "skyrimspecialedition",
            ModId = 3863,
            FileId = 1000172397,
            Version = "5.1",
            Repository = "Nexus",
            Url = "https://www.nexusmods.com/skyrimspecialedition/mods/3863",
            Notes = "test note",
        };

        var manifest = MakeManifest(archives: new[]
        {
            MakeArchive("nexus_skyrimspecialedition_3863_1000172397",
                "SkyUI.7z", meta),
        });

        var output = await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("SkyUI.7z");
        output.Skipped.Should().BeEmpty();

        var path = MetaPath("SkyUI.7z");
        File.Exists(path).Should().BeTrue();

        var read = MetaIniReader.TryRead(path);
        read.Should().NotBeNull();
        read!.ModId.Should().Be(3863);
        read.FileId.Should().Be(1000172397);
        read.Version.Should().Be("5.1");
        read.Notes.Should().Be("test note");
    }

    [Fact]
    public async Task Execute_ArchiveWithoutMeta_Skipped()
    {
        var manifest = MakeManifest(archives: new[]
        {
            MakeArchive("local_somemod", "SomeMod.7z", meta: null),
        });

        var output = await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle().Which.Should().Be("SomeMod.7z");

        File.Exists(MetaPath("SomeMod.7z")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_MultipleArchives_MixedWrittenSkipped()
    {
        var meta = new ModMeta { ModId = 1, FileId = 2 };

        var manifest = MakeManifest(archives: new[]
        {
            MakeArchive("nexus_1_1", "A.7z", meta),
            MakeArchive("local_b", "B.7z", meta: null),
            MakeArchive("nexus_3_3", "C.7z", meta),
        });

        var output = await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEquivalentTo(new[] { "A.7z", "C.7z" });
        output.Skipped.Should().ContainSingle().Which.Should().Be("B.7z");

        File.Exists(MetaPath("A.7z")).Should().BeTrue();
        File.Exists(MetaPath("B.7z")).Should().BeFalse();
        File.Exists(MetaPath("C.7z")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Mo2ArchiveWithMeta_WritesMo2Meta()
    {
        var meta = new ModMeta
        {
            GameName = "Skyrim Special Edition",
            ModId = 0,
            FileId = 0,
        };

        var manifest = MakeManifest(
            archives: Array.Empty<ArchiveEntry>(),
            mo2Archive: MakeMo2Archive(meta));

        var output = await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        output.Written.Should()
            .ContainSingle().Which.Should().Be("Mod.Organizer-2.5.2.7z");

        File.Exists(MetaPath("Mod.Organizer-2.5.2.7z")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_EmptyArchives_NoOp()
    {
        var manifest = MakeManifest();

        var output = await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Idempotency
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Twice_Idempotent()
    {
        var meta = new ModMeta { ModId = 1, FileId = 2 };
        var manifest = MakeManifest(archives: new[]
        {
            MakeArchive("nexus_1_1", "A.7z", meta),
        });

        var input = MakeInput(manifest);

        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        var second = await _step.ExecuteAsync(input, CancellationToken.None);

        first.Written.Should().ContainSingle();
        second.Written.Should().ContainSingle();

        var read = MetaIniReader.TryRead(MetaPath("A.7z"));
        read!.ModId.Should().Be(1);
        read.FileId.Should().Be(2);
    }

    [Fact]
    public async Task Execute_ExistingMetaDifferentContent_Overwrites()
    {
        File.WriteAllText(MetaPath("A.7z"), "old content");

        var meta = new ModMeta { ModId = 42, FileId = 43 };
        var manifest = MakeManifest(archives: new[]
        {
            MakeArchive("nexus_42_43", "A.7z", meta),
        });

        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var read = MetaIniReader.TryRead(MetaPath("A.7z"));
        read!.ModId.Should().Be(42);
        read.FileId.Should().Be(43);
    }

    // ------------------------------------------------------------------
    //  Errors
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MissingDownloads_Throws()
    {
        Directory.Delete(_downloadsDir, recursive: true);

        var manifest = MakeManifest();

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage("*downloads*");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifest = MakeManifest();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
