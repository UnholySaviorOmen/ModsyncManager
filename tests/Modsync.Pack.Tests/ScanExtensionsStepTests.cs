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

public class ScanExtensionsStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instanceDir;
    private readonly string _mo2Dir;
    private readonly FileHashCache _cache = new();
    private readonly ScanExtensionsStep _step;

    public ScanExtensionsStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-se-" + Guid.NewGuid());
        _instanceDir = Path.Combine(_tempDir, "Instance");
        _mo2Dir = Path.Combine(_instanceDir, "MO2");
        Directory.CreateDirectory(_mo2Dir);

        _step = new ScanExtensionsStep(
            _cache, NullLogger<ScanExtensionsStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private void WriteMo2File(string relativePath, string content)
    {
        var fullPath = Path.Combine(
            _mo2Dir,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static PackConfig MakeConfig(params string[] extensions) => new()
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
            Extensions = extensions,
        },
        StockGame = new PackStockGame { Extras = Array.Empty<string>() },
        ArchiveSources = Array.Empty<PackArchiveSource>(),
    };

    private ScanExtensionsStep.Input MakeInput(params string[] extensions)
        => new()
        {
            Config = MakeConfig(extensions),
            Snapshot = MakeSnapshot(),
            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
        };

    private InstanceSnapshot MakeSnapshot() => new()
    {
        InstancePath = _instanceDir,
        Mo2Path = _mo2Dir,
        DownloadsPath = Path.Combine(_mo2Dir, "downloads"),
        ModsPath = Path.Combine(_mo2Dir, "mods"),
        ProfilesPath = Path.Combine(_mo2Dir, "profiles"),
        StockGamePath = Path.Combine(_instanceDir, "Stock Game"),
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
    public async Task Execute_EmptyExtensions_ReturnsEmptyResult()
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
    public async Task Execute_SingleFile_ScannedWithRelativePathFromMo2Root()
    {
        WriteMo2File("plugins/fomod.dll", "fake dll");

        var result = await _step.ExecuteAsync(
            MakeInput("plugins/fomod.dll"), CancellationToken.None);

        result.Entries.Should().ContainKey("plugins/fomod.dll");
        var files = result.Entries["plugins/fomod.dll"];
        files.Should().HaveCount(1);
        files[0].RelativePath.Should().Be("plugins/fomod.dll");
        files[0].Size.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    //  Одна папка
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Directory_RecursesAndKeepsRelativePathFromRoot()
    {
        WriteMo2File("tools/BethINI/BethINI.exe", "exe");
        WriteMo2File("tools/BethINI/readme.txt", "readme");
        WriteMo2File("tools/BethINI/sub/data.bin", "bin");

        var result = await _step.ExecuteAsync(
            MakeInput("tools/BethINI"), CancellationToken.None);

        result.Entries.Should().ContainKey("tools/BethINI");
        var files = result.Entries["tools/BethINI"];
        files.Should().HaveCount(3);

        files.Select(f => f.RelativePath).Should().Equal(
            "tools/BethINI/BethINI.exe",
            "tools/BethINI/readme.txt",
            "tools/BethINI/sub/data.bin");
    }

    // ------------------------------------------------------------------
    //  Trailing slash
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TrailingSlash_Normalized()
    {
        WriteMo2File("tools/BethINI/BethINI.exe", "exe");

        var result = await _step.ExecuteAsync(
            MakeInput("tools/BethINI/"), CancellationToken.None);

        result.Entries.Should().ContainKey("tools/BethINI/");
        var files = result.Entries["tools/BethINI/"];
        files[0].RelativePath.Should().Be("tools/BethINI/BethINI.exe");
    }

    // ------------------------------------------------------------------
    //  Backslash в entry
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_BackslashInEntry_Normalized()
    {
        WriteMo2File("plugins/fomod.dll", "fake dll");

        var result = await _step.ExecuteAsync(
            MakeInput(@"plugins\fomod.dll"), CancellationToken.None);

        var files = result.Entries[@"plugins\fomod.dll"];
        files[0].RelativePath.Should().Be("plugins/fomod.dll");
    }

    // ------------------------------------------------------------------
    //  Несколько entries
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MultipleEntries_AllScanned_PreservingConfigOrder()
    {
        WriteMo2File("plugins/a.dll", "a");
        WriteMo2File("tools/B/b.exe", "b");

        var result = await _step.ExecuteAsync(
            MakeInput("tools/B", "plugins/a.dll"),
            CancellationToken.None);

        result.Entries.Keys.Should().Equal("tools/B", "plugins/a.dll");
    }

    // ------------------------------------------------------------------
    //  Ошибки
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryNotFound_Throws()
    {
        var act = async () => await _step.ExecuteAsync(
            MakeInput("plugins/nope.dll"), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*plugins/nope.dll*");
    }

    [Fact]
    public async Task Execute_EntryDirectoryNotFound_Throws()
    {
        var act = async () => await _step.ExecuteAsync(
            MakeInput("tools/NoSuchDir"), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*tools/NoSuchDir*");
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

    // ------------------------------------------------------------------
    //  Hash cache
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_SameFileInTwoExtensions_UsesSameHash()
    {
        WriteMo2File("a/shared.dll", "same content");
        WriteMo2File("b/shared.dll", "same content");

        var result = await _step.ExecuteAsync(
            MakeInput("a/shared.dll", "b/shared.dll"),
            CancellationToken.None);

        var h1 = result.Entries["a/shared.dll"][0].Hash;
        var h2 = result.Entries["b/shared.dll"][0].Hash;
        h1.Should().Be(h2);
    }
}
