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

public class GenerateMetaIniStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _modsDir;
    private readonly GenerateMetaIniStep _step;

    public GenerateMetaIniStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-gmi-" + Guid.NewGuid());
        _modsDir = Path.Combine(_tempDir, "mods");
        Directory.CreateDirectory(_modsDir);

        _step = new GenerateMetaIniStep(
            NullLogger<GenerateMetaIniStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private void CreateModFolder(string name)
    {
        Directory.CreateDirectory(Path.Combine(_modsDir, name));
    }

    private string MetaIniPath(string modName)
        => Path.Combine(_modsDir, modName, "meta.ini");

    private static ModEntry MakeMod(
        string name,
        bool enabled = true,
        int order = 0,
        ModMeta? meta = null)
        => new()
        {
            Name = name,
            Enabled = enabled,
            Order = order,
            Meta = meta,
            Directives = Array.Empty<Modsync.Core.Models.Manifest.Directives.Directive>(),
        };

    private static ModlistManifest MakeManifest(params ModEntry[] mods)
    {
        return new ModlistManifest
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
            Mods = mods,
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };
    }

    private GenerateMetaIniStep.Input MakeInput(ModlistManifest manifest)
        => new()
        {
            Manifest = manifest,
            ModsPath = _modsDir,
        };

    [Fact]
    public async Task Execute_ModWithMeta_WritesMetaIni()
    {
        CreateModFolder("SkyUI");
        var meta = new ModMeta { ModId = 1, Version = "1.0" };

        var manifest = MakeManifest(MakeMod("SkyUI", meta: meta));
        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("SkyUI");
        output.Deleted.Should().BeEmpty();

        var path = MetaIniPath("SkyUI");
        File.Exists(path).Should().BeTrue();

        var read = MetaIniReader.TryRead(path);
        read.Should().NotBeNull();
        read!.ModId.Should().Be(1);
        read.Version.Should().Be("1.0");
    }

    [Fact]
    public async Task Execute_EmptyMeta_WritesGeneralOnly()
    {
        CreateModFolder("Mod");
        var manifest = MakeManifest(MakeMod("Mod", meta: ModMeta.Empty));

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().ContainSingle();

        var text = File.ReadAllText(MetaIniPath("Mod"));
        text.Should().Be("[General]\r\n");
    }

    [Fact]
    public async Task Execute_ExistingMetaIni_Overwritten()
    {
        CreateModFolder("Mod");
        File.WriteAllText(MetaIniPath("Mod"), "old content");

        var meta = new ModMeta { ModId = 42 };
        var manifest = MakeManifest(MakeMod("Mod", meta: meta));

        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var text = File.ReadAllText(MetaIniPath("Mod"));
        text.Should().Contain("modID=42");
        text.Should().NotContain("old content");
    }

    [Fact]
    public async Task Execute_ModWithoutMeta_NoFile_NothingDone()
    {
        CreateModFolder("Mod");
        var manifest = MakeManifest(MakeMod("Mod", meta: null));

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Deleted.Should().BeEmpty();
        File.Exists(MetaIniPath("Mod")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_ModWithoutMeta_FileExists_Deleted()
    {
        CreateModFolder("Mod");
        File.WriteAllText(MetaIniPath("Mod"), "[General]\nmodID=99");

        var manifest = MakeManifest(MakeMod("Mod", meta: null));
        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Deleted.Should().ContainSingle().Which.Should().Be("Mod");
        File.Exists(MetaIniPath("Mod")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_NoDeleteMod_Skipped()
    {
        CreateModFolder("[NoDelete]Protected");
        File.WriteAllText(MetaIniPath("[NoDelete]Protected"), "user meta");

        var meta = new ModMeta { ModId = 1 };
        var manifest = MakeManifest(
            MakeMod("[NoDelete]Protected", meta: meta));

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Deleted.Should().BeEmpty();

        File.ReadAllText(MetaIniPath("[NoDelete]Protected")).Should().Be("user meta");
    }

    [Fact]
    public async Task Execute_ModFolderMissing_SkippedWithWarning()
    {
        var meta = new ModMeta { ModId = 1 };
        var manifest = MakeManifest(MakeMod("Nonexistent", meta: meta));

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_MultipleMods_Mixed()
    {
        CreateModFolder("ModA");
        CreateModFolder("ModB");
        CreateModFolder("ModC");
        File.WriteAllText(MetaIniPath("ModC"), "stale");

        var manifest = MakeManifest(
            MakeMod("ModA", meta: new ModMeta { ModId = 1 }),
            MakeMod("ModB", meta: null),
            MakeMod("ModC", meta: null));

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("ModA");
        output.Deleted.Should().ContainSingle().Which.Should().Be("ModC");
    }

    [Fact]
    public async Task Execute_EmptyManifest_NoOp()
    {
        var manifest = MakeManifest();

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WrittenFile_ReadableByReader()
    {
        CreateModFolder("Mod");
        var original = new ModMeta
        {
            GameName = "Skyrim Special Edition",
            GameId = "skyrimspecialedition",
            ModId = 32349,
            FileId = 795423,
            Version = "1.7.0",
            Repository = "Nexus",
            Url = "https://www.nexusmods.com/skyrimspecialedition/mods/32349",
            Comments = "",
            Notes = "note",
        };

        var manifest = MakeManifest(MakeMod("Mod", meta: original));
        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        var read = MetaIniReader.TryRead(MetaIniPath("Mod"));

        read.Should().NotBeNull();
        read!.ModId.Should().Be(32349);
        read.FileId.Should().Be(795423);
        read.Version.Should().Be("1.7.0");
        read.Notes.Should().Be("note");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifest = MakeManifest();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(MakeInput(manifest), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
