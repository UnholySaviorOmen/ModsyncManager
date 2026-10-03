// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack;
using Modsync.Pack.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class PackConfigBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _downloadsPath;
    private readonly string _profilesPath;
    private readonly FileHashCache _hashCache = new();
    private readonly PackConfigBuilder _builder;

    public PackConfigBuilderTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-packbuilder-" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "TestInstance");
        _downloadsPath = Path.Combine(_instancePath, "MO2", "downloads");
        _profilesPath = Path.Combine(_instancePath, "MO2", "profiles");

        Directory.CreateDirectory(_downloadsPath);
        Directory.CreateDirectory(_profilesPath);

        _builder = new PackConfigBuilder(
            _hashCache,
            NullLogger<PackConfigBuilder>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private void WriteArchive(string name, string content = "fake archive")
    {
        File.WriteAllText(Path.Combine(_downloadsPath, name), content);
    }

    private void WriteValidMeta(string archiveName, int modId, int fileId)
    {
        File.WriteAllText(
            Path.Combine(_downloadsPath, archiveName + ".meta"),
            $"[General]\r\ngameName=Skyrim\r\nmodID={modId}\r\nfileID={fileId}\r\n");
    }

    private void WriteInvalidMeta(string archiveName)
    {
        File.WriteAllText(
            Path.Combine(_downloadsPath, archiveName + ".meta"),
            "[General]\r\ndirectURL=https://example.com/foo.7z\r\n");
    }

    private void WriteProfile(string name)
    {
        Directory.CreateDirectory(Path.Combine(_profilesPath, name));
    }

    // ------------------------------------------------------------------
    //  ScanDownloadsAsync — empty
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScanDownloads_EmptyDirectory_ReturnsEmpty()
    {
        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanDownloads_MissingDownloads_Throws()
    {
        Directory.Delete(_downloadsPath, recursive: true);

        var act = async () => await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task ScanDownloads_EmptyPath_Throws()
    {
        var act = async () => await _builder.ScanDownloadsAsync(
            "", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------------
    //  ScanDownloadsAsync — nexus-only
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScanDownloads_AllNexusWithMeta_ReturnsEmpty()
    {
        WriteArchive("SkyUI.7z");
        WriteValidMeta("SkyUI.7z", 3863, 1000172397);

        WriteArchive("Another.7z");
        WriteValidMeta("Another.7z", 12345, 67890);

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  ScanDownloadsAsync — non-nexus
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScanDownloads_NoMetaAtAll_ReturnsAll()
    {
        WriteArchive("SomeMod.7z");
        WriteArchive("AnotherMod.7z");

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(a => a.FileName).Should().Equal(
            "AnotherMod.7z", "SomeMod.7z");
    }

    [Fact]
    public async Task ScanDownloads_InvalidMeta_ReturnsAsUnresolved()
    {
        WriteArchive("Bad.7z");
        WriteInvalidMeta("Bad.7z");

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("Bad.7z");
    }

    [Fact]
    public async Task ScanDownloads_Mixed_ReturnsOnlyNonNexus()
    {
        WriteArchive("SkyUI.7z");
        WriteValidMeta("SkyUI.7z", 3863, 1000172397);

        WriteArchive("CustomMod.7z");
        // без .meta

        WriteArchive("InvalidMetaMod.7z");
        WriteInvalidMeta("InvalidMetaMod.7z");

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(a => a.FileName).Should().Equal(
            "CustomMod.7z", "InvalidMetaMod.7z");
    }

    [Fact]
    public async Task ScanDownloads_IgnoresNonArchiveFiles()
    {
        WriteArchive("readme.txt", "hello");
        WriteArchive("notes.md", "# notes");
        WriteArchive("some.meta", "[General]\r\nmodID=1\r\nfileID=2\r\n");

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanDownloads_HashAndSizeArePopulated()
    {
        WriteArchive("SomeMod.7z", "hello world");

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Size.Should().Be(11);
        result[0].Hash.Value.Should().NotBe(0);
        result[0].FullPath.Should().Be(Path.Combine(_downloadsPath, "SomeMod.7z"));
    }

    [Fact]
    public async Task ScanDownloads_SortedByFileName()
    {
        WriteArchive("Zeta.7z");
        WriteArchive("Alpha.7z");
        WriteArchive("Mu.7z");

        var result = await _builder.ScanDownloadsAsync(
            _instancePath, CancellationToken.None);

        result.Select(a => a.FileName).Should().Equal(
            "Alpha.7z", "Mu.7z", "Zeta.7z");
    }

    [Fact]
    public async Task ScanDownloads_CanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _builder.ScanDownloadsAsync(
            _instancePath, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    //  ListProfiles
    // ------------------------------------------------------------------

    [Fact]
    public void ListProfiles_Empty_ReturnsEmpty()
    {
        var result = _builder.ListProfiles(_instancePath);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ListProfiles_Multiple_ReturnsSorted()
    {
        WriteProfile("NordicUI");
        WriteProfile("Default");
        WriteProfile("Requiem");

        var result = _builder.ListProfiles(_instancePath);

        result.Should().Equal("Default", "NordicUI", "Requiem");
    }

    [Fact]
    public void ListProfiles_MissingProfilesFolder_ReturnsEmpty()
    {
        Directory.Delete(_profilesPath, recursive: true);

        var result = _builder.ListProfiles(_instancePath);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ListProfiles_EmptyPath_Throws()
    {
        var act = () => _builder.ListProfiles("");
        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------
    //  Build
    // ------------------------------------------------------------------

    private static PackMeta MakeMeta(
        string name = "Test Pack",
        string version = "1.0.0",
        string author = "tester",
        string game = "skyrimspecialedition",
        string gameVersion = "1.6.1170") => new()
        {
            Name = name,
            Version = version,
            Author = author,
            Game = game,
            GameVersion = gameVersion,
        };

    [Fact]
    public void Build_MinimalInput_ProducesValidConfig()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var config = _builder.Build(input);

        config.Meta.Name.Should().Be("Test Pack");
        config.Meta.Version.Should().Be("1.0.0");
        config.Instance.Path.Should().Be(".");
        config.Mo2.Version.Should().Be("2.5.2");
        config.Mo2.Profile.Should().Be("Default");
        config.Mo2.Archive.Should().Be("Mod.Organizer-2.5.2.7z");
        config.Mo2.Source.Should().BeOfType<MirrorSourceRef>();
        config.Mo2.Extensions.Should().BeEmpty();
        config.StockGame.Extras.Should().BeEmpty();
        config.ArchiveSources.Should().BeEmpty();
    }

    [Fact]
    public void Build_Mo2Section_IsHardcoded()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var config = _builder.Build(input);

        config.Mo2.Version.Should().Be(PackConfigBuilder.Mo2Version);
        config.Mo2.Archive.Should().Be(PackConfigBuilder.Mo2ArchiveName);

        var mirror = config.Mo2.Source.Should().BeOfType<MirrorSourceRef>().Subject;
        mirror.Url.Should().Be(PackConfigBuilder.Mo2Url);
        mirror.Hash.Should().Be(PackConfigBuilder.Mo2Hash);
    }

    [Fact]
    public void Build_EmptyProfile_DefaultsToDefault()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var config = _builder.Build(input);

        config.Mo2.Profile.Should().Be("Default");
    }

    [Fact]
    public void Build_CustomProfile_Preserved()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "NordicUI",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var config = _builder.Build(input);

        config.Mo2.Profile.Should().Be("NordicUI");
    }

    [Fact]
    public void Build_WithExtensionsAndExtras_Propagated()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "Default",
            Extensions = new[] { "plugins/fomod.dll", "tools/BethINI/" },
            Extras = new[] { "skse64_loader.exe", "enbseries/" },
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var config = _builder.Build(input);

        config.Mo2.Extensions.Should().Equal("plugins/fomod.dll", "tools/BethINI/");
        config.StockGame.Extras.Should().Equal("skse64_loader.exe", "enbseries/");
    }

    [Fact]
    public void Build_WithArchiveSources_Propagated()
    {
        var source = new PackArchiveSource
        {
            Archive = "CustomMod.7z",
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef
                {
                    Url = "https://example.com/CustomMod.7z",
                    Hash = new XxHash64Value(0xabc),
                },
            },
        };

        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = new[] { source },
        };

        var config = _builder.Build(input);

        config.ArchiveSources.Should().HaveCount(1);
        config.ArchiveSources[0].Archive.Should().Be("CustomMod.7z");
        config.ArchiveSources[0].Sources.Should().HaveCount(1);
    }

    [Fact]
    public void Build_BuiltConfig_PassesValidator()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = _instancePath,
            Meta = MakeMeta(),
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var config = _builder.Build(input);
        var result = Modsync.Core.Validation.PackConfigValidator.Validate(config);

        result.IsValid.Should().BeTrue(
            $"errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Build_NullInput_Throws()
    {
        var act = () => _builder.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_EmptyInstancePath_Throws()
    {
        var input = new PackConfigBuilderInput
        {
            InstancePath = "",
            Meta = MakeMeta(),
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<PackArchiveSource>(),
        };

        var act = () => _builder.Build(input);
        act.Should().Throw<ArgumentException>();
    }
}
