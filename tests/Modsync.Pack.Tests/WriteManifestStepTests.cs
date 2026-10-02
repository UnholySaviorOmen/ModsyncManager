// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class WriteManifestStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _outputDir;
    private readonly WriteManifestStep _step;

    public WriteManifestStepTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "modsyncmanager-wm-" + Guid.NewGuid());
        _outputDir = Path.Combine(_tempDir, "__ModsyncManager_Output");
        Directory.CreateDirectory(_outputDir);
        _step = new WriteManifestStep(NullLogger<WriteManifestStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private static ModlistManifest MakeManifest(string name = "Test Pack")
        => new()
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = name,
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
                Archive = new ArchiveEntry
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
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = new[]
            {
                new ModEntry
                {
                    Name = "SkyUI",
                    Enabled = true,
                    Order = 0,
                    Meta = new ModMeta
                    {
                        ModId = 3863,
                        FileId = 1000172397,
                        Version = "5.1",
                        Notes = "my note",
                    },
                    Directives = new Directive[]
                    {
                        new FromArchiveDirective
                        {
                            Archive = "nexus_skyrimspecialedition_3863_1000172397",
                            Source = "interface/iconmenu.swf",
                            Destination = "interface/iconmenu.swf",
                            Hash = new XxHash64Value(0x1234),
                            Size = 100,
                        },
                    },
                },
            },
            Plugins = new[]
            {
                new PluginEntry { Name = "SkyUI.esp", Enabled = true, Order = 0 },
            },
            Loadorder = new[] { "Skyrim.esm", "SkyUI.esp" },
        };

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WritesManifestToModsyncOutput()
    {
        var manifest = MakeManifest();

        var path = await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = manifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        path.Should().Be(Path.Combine(_outputDir, "modlist.json"));
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ReturnsFilePath()
    {
        var manifest = MakeManifest();

        var path = await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = manifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        path.Should().StartWith(_outputDir);
        path.Should().EndWith("modlist.json");
    }

    [Fact]
    public async Task Execute_ValidJson_CanBeDeserializedBack()
    {
        var manifest = MakeManifest(name: "Roundtrip Test");

        var path = await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = manifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        var json = File.ReadAllText(path);

        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();

        var back = ManifestJson.Deserialize(json);
        back.Meta.Name.Should().Be("Roundtrip Test");
        back.Mods.Should().HaveCount(1);
        back.Mods[0].Name.Should().Be("SkyUI");
        back.Mods[0].Meta.Should().NotBeNull();
        back.Mods[0].Meta!.ModId.Should().Be(3863);
    }

    [Fact]
    public async Task Execute_ModsWithMetaIncluded()
    {
        var manifest = MakeManifest();

        var path = await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = manifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        var back = ManifestJson.Deserialize(File.ReadAllText(path));

        back.Mods[0].Meta.Should().NotBeNull();
        back.Mods[0].Meta!.Version.Should().Be("5.1");
        back.Mods[0].Meta!.Notes.Should().Be("my note");
    }

    [Fact]
    public async Task Execute_ModsWithoutMeta_OmitMetaField()
    {
        var baseManifest = MakeManifest();

        var newManifest = new ModlistManifest
        {
            SchemaVersion = baseManifest.SchemaVersion,
            ManifestVersion = baseManifest.ManifestVersion,
            CreatedAt = baseManifest.CreatedAt,
            CreatedBy = baseManifest.CreatedBy,
            Meta = baseManifest.Meta,
            Execution = baseManifest.Execution,
            Mo2 = baseManifest.Mo2,
            StockGame = baseManifest.StockGame,
            Archives = baseManifest.Archives,
            Mods = new[]
            {
                new ModEntry
                {
                    Name = "NoMetaMod",
                    Enabled = true,
                    Order = 0,
                    Meta = null,
                    Directives = Array.Empty<Directive>(),
                },
            },
            Plugins = baseManifest.Plugins,
            Loadorder = baseManifest.Loadorder,
        };

        var path = await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = newManifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        var json = File.ReadAllText(path);

        json.Should().NotContain("\"meta\": null");

        var back = ManifestJson.Deserialize(json);
        back.Mods[0].Meta.Should().BeNull();
    }

    [Fact]
    public async Task Execute_EmptyModsyncOutput_Throws()
    {
        var missingDir = Path.Combine(_tempDir, "does-not-exist");

        var act = async () => await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = MakeManifest(),
                ModsyncOutputPath = missingDir,
            }, CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage("*__ModsyncManager_Output*");
    }

    [Fact]
    public async Task Execute_ExistingManifest_Overwrites()
    {
        var manifestPath = Path.Combine(_outputDir, "modlist.json");
        File.WriteAllText(manifestPath, "garbage");

        var manifest = MakeManifest(name: "New Name");

        await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = manifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        var json = File.ReadAllText(manifestPath);
        json.Should().Contain("New Name");
        json.Should().NotContain("garbage");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifest = MakeManifest();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = manifest,
                ModsyncOutputPath = _outputDir,
            }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Execute_ManifestWithNoMods_WritesSuccessfully()
    {
        var baseManifest = MakeManifest();

        var emptyManifest = new ModlistManifest
        {
            SchemaVersion = baseManifest.SchemaVersion,
            ManifestVersion = baseManifest.ManifestVersion,
            CreatedAt = baseManifest.CreatedAt,
            CreatedBy = baseManifest.CreatedBy,
            Meta = baseManifest.Meta,
            Execution = baseManifest.Execution,
            Mo2 = baseManifest.Mo2,
            StockGame = baseManifest.StockGame,
            Archives = baseManifest.Archives,
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

        var path = await _step.ExecuteAsync(
            new WriteManifestStep.Input
            {
                Manifest = emptyManifest,
                ModsyncOutputPath = _outputDir,
            }, CancellationToken.None);

        File.Exists(path).Should().BeTrue();

        var back = ManifestJson.Deserialize(File.ReadAllText(path));
        back.Mods.Should().BeEmpty();
    }
}
