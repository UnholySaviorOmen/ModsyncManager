// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using FluentAssertions;
using Modsync.Pack;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class PatchArchiveBuilderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _outputPath;
    private readonly string _downloadsPath;
    private readonly PatchArchiveBuilder _builder;

    public PatchArchiveBuilderTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-patch-" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "Instance");
        _outputPath = Path.Combine(_instancePath, "__ModsyncManager_Output");
        _downloadsPath = Path.Combine(_instancePath, "MO2", "downloads");

        Directory.CreateDirectory(_outputPath);
        Directory.CreateDirectory(_downloadsPath);

        _builder = new PatchArchiveBuilder(
            NullLogger<PatchArchiveBuilder>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private void WriteOutputFile(string relativePath, string content = "data")
    {
        var fullPath = Path.Combine(
            _outputPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static IReadOnlyList<string> ListEntries(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries
            .Select(e => e.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    // ------------------------------------------------------------------
    //  Empty / missing
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_MissingOutputDir_ReturnsNull()
    {
        Directory.Delete(_outputPath, recursive: true);

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Build_EmptyOutputDir_ReturnsNull()
    {
        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Build_OnlyManifest_ReturnsNull()
    {
        WriteOutputFile("modlist.json", "{}");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Build_EmptyPath_Throws()
    {
        var act = async () => await _builder.BuildAsync("", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Build_CanceledToken_Throws()
    {
        WriteOutputFile("MO2/mods/Mod/file.txt");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _builder.BuildAsync(_instancePath, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    //  Only mods
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_OnlyMods_PacksWithCorrectStructure()
    {
        WriteOutputFile("MO2/mods/Actor Limit Fix/ActorLimitFix.dll");
        WriteOutputFile("MO2/mods/Actor Limit Fix/readme.txt");
        WriteOutputFile("MO2/mods/Bug Fixes SSE/BugFixes.esp");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        File.Exists(result!).Should().BeTrue();

        var entries = ListEntries(result!);
        entries.Should().Equal(
            "MO2/mods/Actor Limit Fix/ActorLimitFix.dll",
            "MO2/mods/Actor Limit Fix/readme.txt",
            "MO2/mods/Bug Fixes SSE/BugFixes.esp");
    }

    // ------------------------------------------------------------------
    //  Mixed categories
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_MixedCategories_PacksAll()
    {
        WriteOutputFile("MO2/mods/Actor Limit Fix/file.dll");
        WriteOutputFile("MO2/plugins/fomod.dll");
        WriteOutputFile("MO2/tools/MyPatcher/MyPatcher.exe");
        WriteOutputFile("Stock Game/skse64_loader.exe");
        WriteOutputFile("Stock Game/enbseries/enb.ini");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        var entries = ListEntries(result!);
        entries.Should().Equal(
            "MO2/mods/Actor Limit Fix/file.dll",
            "MO2/plugins/fomod.dll",
            "MO2/tools/MyPatcher/MyPatcher.exe",
            "Stock Game/enbseries/enb.ini",
            "Stock Game/skse64_loader.exe");
    }

    // ------------------------------------------------------------------
    //  modlist.json excluded
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_ManifestNotPacked()
    {
        WriteOutputFile("modlist.json", "{}");
        WriteOutputFile("MO2/mods/Mod/file.txt");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        var entries = ListEntries(result!);
        entries.Should().NotContain("modlist.json");
        entries.Should().Contain("MO2/mods/Mod/file.txt");
    }

    [Fact]
    public async Task Build_ManifestCaseInsensitive_NotPacked()
    {
        WriteOutputFile("MODLIST.JSON", "{}");
        WriteOutputFile("MO2/mods/Mod/file.txt");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        var entries = ListEntries(result!);
        entries.Should().NotContain("MODLIST.JSON");
    }

    // ------------------------------------------------------------------
    //  Target location
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_ArchivePlacedInDownloads()
    {
        WriteOutputFile("MO2/mods/Mod/file.txt");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Be(Path.Combine(_downloadsPath, PatchArchiveBuilder.PatchArchiveName));
        File.Exists(result!).Should().BeTrue();
    }

    [Fact]
    public async Task Build_DownloadsMissing_Creates()
    {
        Directory.Delete(_downloadsPath, recursive: true);
        WriteOutputFile("MO2/mods/Mod/file.txt");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        Directory.Exists(_downloadsPath).Should().BeTrue();
        File.Exists(result!).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Overwrite
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_CalledTwice_Overwrites()
    {
        WriteOutputFile("MO2/mods/ModA/file-a.txt", "first");

        var first = await _builder.BuildAsync(_instancePath, CancellationToken.None);
        first.Should().NotBeNull();
        var firstSize = new FileInfo(first!).Length;

        // Меняем содержимое: добавляем ещё файл.
        WriteOutputFile("MO2/mods/ModB/file-b.txt", "second");

        var second = await _builder.BuildAsync(_instancePath, CancellationToken.None);
        second.Should().NotBeNull();

        // Тот же путь — перезаписан.
        second.Should().Be(first);

        // Архив содержит оба файла.
        var entries = ListEntries(second!);
        entries.Should().Contain("MO2/mods/ModA/file-a.txt");
        entries.Should().Contain("MO2/mods/ModB/file-b.txt");
    }

    // ------------------------------------------------------------------
    //  Output dir preserved
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_DoesNotDeleteOutputDir()
    {
        WriteOutputFile("MO2/mods/Mod/file.txt");

        await _builder.BuildAsync(_instancePath, CancellationToken.None);

        Directory.Exists(_outputPath).Should().BeTrue();
        File.Exists(Path.Combine(
            _outputPath, "MO2", "mods", "Mod", "file.txt"))
            .Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Content preserved
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_ContentPreserved()
    {
        WriteOutputFile("MO2/mods/Mod/file.txt", "hello world");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();

        using var zip = ZipFile.OpenRead(result!);
        var entry = zip.GetEntry("MO2/mods/Mod/file.txt");
        entry.Should().NotBeNull();

        using var stream = entry!.Open();
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        content.Should().Be("hello world");
    }

    // ------------------------------------------------------------------
    //  Directory structure with only files
    // ------------------------------------------------------------------

    [Fact]
    public async Task Build_NestedDirs_Preserved()
    {
        WriteOutputFile("MO2/mods/Mod/a/b/c/deep.txt");
        WriteOutputFile("MO2/mods/Mod/top.txt");

        var result = await _builder.BuildAsync(_instancePath, CancellationToken.None);

        result.Should().NotBeNull();
        var entries = ListEntries(result!);
        entries.Should().Contain("MO2/mods/Mod/a/b/c/deep.txt");
        entries.Should().Contain("MO2/mods/Mod/top.txt");
    }
}
