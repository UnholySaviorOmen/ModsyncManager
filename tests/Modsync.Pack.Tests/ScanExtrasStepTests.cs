// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class ScanExtrasStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instanceDir;
    private readonly string _stockGameDir;
    private readonly FileHashCache _cache = new();
    private readonly ScanExtrasStep _step;

    public ScanExtrasStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-sx-" + Guid.NewGuid());
        _instanceDir = Path.Combine(_tempDir, "Instance");
        _stockGameDir = Path.Combine(_instanceDir, "Stock Game");
        Directory.CreateDirectory(_stockGameDir);

        _step = new ScanExtrasStep(
            _cache, NullLogger<ScanExtrasStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private void WriteStockFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(
            _stockGameDir,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static PackConfig MakeConfig(params string[] extras) => new()
    {
        Meta = new PackMeta
        {
            Name = "Test",
            Version = "1.0.0",
            Author = "a",
            Game = "skyrimspecialedition",
            GameVersion = "1.6.1170",
        },
        Instance = new PackInstance { Path = "Instance" },
        Mo2 = new PackMo2
        {
            Version = "2.5.2",
            Profile = "Default",
            Archive = "MO2.7z",
            Source = new MirrorSourceRef
            {
                Url = "https://example.com/MO2.7z",
                Hash = new XxHash64Value(0xabc),
            },
            Extensions = Array.Empty<string>(),
        },
        StockGame = new PackStockGame { Extras = extras },
        ArchiveSources = Array.Empty<PackArchiveSource>(),
    };

    private ScanExtrasStep.Input MakeInput(params string[] extras)
        => new()
        {
            Config = MakeConfig(extras),
            Snapshot = MakeSnapshot(),
            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
        };

    private InstanceSnapshot MakeSnapshot() => new()
    {
        InstancePath = _instanceDir,
        Mo2Path = Path.Combine(_instanceDir, "MO2"),
        DownloadsPath = Path.Combine(_instanceDir, "MO2", "downloads"),
        ModsPath = Path.Combine(_instanceDir, "MO2", "mods"),
        ProfilesPath = Path.Combine(_instanceDir, "MO2", "profiles"),
        StockGamePath = _stockGameDir,
        ModsyncOutputPath = Path.Combine(_instanceDir, "__ModsyncManager_Output"),
        Modlist = new Modsync.Core.Models.Mo2.ModlistFile
        {
            Entries = Array.Empty<Modsync.Core.Models.Mo2.ModlistEntry>(),
        },
        Plugins = new Modsync.Core.Models.Mo2.PluginsFile
        {
            Entries = Array.Empty<Modsync.Core.Models.Mo2.PluginEntry>(),
        },
        Loadorder = new Modsync.Core.Models.Mo2.LoadorderFile
        {
            Plugins = Array.Empty<string>(),
        },
    };

    // ------------------------------------------------------------------
    //  Пустой список
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyExtras_ReturnsEmptyResult()
    {
        var result = await _step.ExecuteAsync(
            MakeInput(), CancellationToken.None);

        result.Entries.Should().BeEmpty();
        result.TotalEntries.Should().Be(0);
        result.TotalFiles.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  Один файл
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_SingleFile_ScannedWithRelativePathFromStockGameRoot()
    {
        WriteStockFile("skse64_loader.exe", "fake exe");

        var result = await _step.ExecuteAsync(
            MakeInput("skse64_loader.exe"), CancellationToken.None);

        result.Entries.Should().ContainKey("skse64_loader.exe");
        var files = result.Entries["skse64_loader.exe"];
        files.Should().HaveCount(1);
        files[0].RelativePath.Should().Be("skse64_loader.exe");
        files[0].Size.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    //  Одна папка
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Directory_RecursesAndKeepsRelativePathFromRoot()
    {
        WriteStockFile("enbseries/enbseries.ini", "ini");
        WriteStockFile("enbseries/readme.txt", "readme");
        WriteStockFile("enbseries/patches/deep.ini", "deep");

        var result = await _step.ExecuteAsync(
            MakeInput("enbseries"), CancellationToken.None);

        result.Entries.Should().ContainKey("enbseries");
        var files = result.Entries["enbseries"];
        files.Should().HaveCount(3);

        files.Select(f => f.RelativePath).Should().Equal(
            "enbseries/enbseries.ini",
            "enbseries/patches/deep.ini",
            "enbseries/readme.txt");
    }

    // ------------------------------------------------------------------
    //  Trailing slash
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TrailingSlash_Normalized()
    {
        WriteStockFile("enbseries/enbseries.ini", "ini");

        var result = await _step.ExecuteAsync(
            MakeInput("enbseries/"), CancellationToken.None);

        var files = result.Entries["enbseries/"];
        files[0].RelativePath.Should().Be("enbseries/enbseries.ini");
    }

    // ------------------------------------------------------------------
    //  Несколько entries
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MultipleEntries_AllScanned_PreservingConfigOrder()
    {
        WriteStockFile("skse64_loader.exe", "exe");
        WriteStockFile("enbseries/enb.ini", "ini");

        var result = await _step.ExecuteAsync(
            MakeInput("enbseries", "skse64_loader.exe"),
            CancellationToken.None);

        result.Entries.Keys.Should().Equal("enbseries", "skse64_loader.exe");
    }

    // ------------------------------------------------------------------
    //  Ошибки
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryNotFound_Throws()
    {
        var act = async () => await _step.ExecuteAsync(
            MakeInput("nope.exe"), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*nope.exe*");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            MakeInput(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
