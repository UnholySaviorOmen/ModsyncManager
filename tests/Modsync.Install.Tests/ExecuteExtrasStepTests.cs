// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class ExecuteExtrasStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instanceDir;
    private readonly string _stockGamePath;
    private readonly string _downloadsPath;
    private readonly FileHashCache _hashCache = new();
    private readonly ExecuteExtrasStep _step;

    public ExecuteExtrasStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-xst-" + Guid.NewGuid());
        _instanceDir = Path.Combine(_tempDir, "instance");
        _stockGamePath = Path.Combine(_instanceDir, "Stock Game");
        _downloadsPath = Path.Combine(_instanceDir, "MO2", "downloads");

        Directory.CreateDirectory(_stockGamePath);
        Directory.CreateDirectory(_downloadsPath);
        // MO2/ нужен, но этот шаг его не трогает.
        Directory.CreateDirectory(Path.Combine(_instanceDir, "MO2"));

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        _step = new ExecuteExtrasStep(
            extractor,
            NullLogger<ExecuteExtrasStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private string StockFile(string relativePath)
        => Path.Combine(_stockGamePath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static ExtensionEntry MakeEntry(
        string name,
        params Directive[] directives) => new()
        {
            Name = name,
            Directives = directives,
        };

    private static FromArchiveDirective MakeFromArchive(
        string archive,
        string source,
        string destination,
        XxHash64Value hash,
        long size) => new()
        {
            Archive = archive,
            Source = source,
            Destination = destination,
            Hash = hash,
            Size = size,
        };

    private ExecuteExtrasStep.Input MakeInput(
        IReadOnlyList<ExtensionEntry>? entries = null,
        IReadOnlyDictionary<string, ArchiveEntry>? archivesById = null)
    {
        var manifest = MakeManifest(entries ?? Array.Empty<ExtensionEntry>());

        return new ExecuteExtrasStep.Input
        {
            InstancePath = _instanceDir,
            Manifest = manifest,
            DownloadsPath = _downloadsPath,
            ArchivesById = archivesById
                ?? new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal),
        };
    }

    private static ModlistManifest MakeManifest(
        IReadOnlyList<ExtensionEntry> extras)
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
                    Sources = Array.Empty<
                        Core.Models.Manifest.Sources.ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection { Extras = extras },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };
    }

    private Dictionary<string, ArchiveEntry> MakeArchivesById(
        params ArchiveEntry[] entries)
    {
        var dict = new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
            dict[e.Id] = e;
        return dict;
    }

    // ------------------------------------------------------------------
    //  1. Пустой массив entries
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyManifestEntries_NoOp()
    {
        var output = await _step.ExecuteAsync(
            MakeInput(), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  2. Файла нет → Written
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileMissing_CopiedAndWritten()
    {
        var content = Encoding.UTF8.GetBytes("skse64_loader.exe content");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip", ("skse64_loader.exe", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse64_loader.exe", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("skse");
        File.Exists(StockFile("skse64_loader.exe")).Should().BeTrue();
        TestArchives.ReadBytes(StockFile("skse64_loader.exe"))
            .Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  3. Файл есть, hash совпадает → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileMatchesHash_Skipped()
    {
        var content = Encoding.UTF8.GetBytes("d3d11.dll content");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip", ("d3d11.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        File.WriteAllBytes(StockFile("d3d11.dll"), content);

        var entry = MakeEntry("enb",
            MakeFromArchive("local_enb", "d3d11.dll",
                "d3d11.dll", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle().Which.Should().Be("enb");
    }

    // ------------------------------------------------------------------
    //  4. Файл есть, hash не тот → Written, перезаписан
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileWrongHash_OverwrittenAndWritten()
    {
        var content = Encoding.UTF8.GetBytes("community shaders d3d11.dll");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "cs.zip", ("d3d11.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_cs", _hashCache);

        // Старый ENB-ный d3d11.dll.
        File.WriteAllBytes(StockFile("d3d11.dll"),
            Encoding.UTF8.GetBytes("old enb d3d11.dll"));

        var entry = MakeEntry("cs",
            MakeFromArchive("local_cs", "d3d11.dll",
                "d3d11.dll", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        TestArchives.ReadBytes(StockFile("d3d11.dll")).Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  5. Часть файлов совпадает → Written только за недостающие
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PartialMatch_OnlyMissingCopied()
    {
        var content1 = Encoding.UTF8.GetBytes("skse64_loader.exe");
        var content2 = Encoding.UTF8.GetBytes("skse64_1_6_1170.dll");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip",
            ("skse64_loader.exe", content1),
            ("skse64_1_6_1170.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        // loader уже на месте, dll — нет.
        File.WriteAllBytes(StockFile("skse64_loader.exe"), content1);

        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse64_loader.exe",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_skse", "skse64_1_6_1170.dll",
                "skse64_1_6_1170.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("skse");
        File.Exists(StockFile("skse64_loader.exe")).Should().BeTrue();
        File.Exists(StockFile("skse64_1_6_1170.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  6. Несколько entries → соответствующие Written/Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MultipleEntries_MixedWrittenSkipped()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "stuff.zip",
            ("a.exe", contentA),
            ("b.dll", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_stuff", _hashCache);

        File.WriteAllBytes(StockFile("a.exe"), contentA);

        var entryA = MakeEntry("stuff-a",
            MakeFromArchive("local_stuff", "a.exe", "a.exe",
                TestArchives.HashOf(contentA), contentA.Length));
        var entryB = MakeEntry("stuff-b",
            MakeFromArchive("local_stuff", "b.dll", "b.dll",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entryA, entryB },
                MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("stuff-b");
        output.Skipped.Should().ContainSingle().Which.Should().Be("stuff-a");
    }

    // ------------------------------------------------------------------
    //  7. Destination с подпапкой → папки создаются
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DestinationWithSubdir_CreatesFolders()
    {
        var content = Encoding.UTF8.GetBytes("enb settings");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/enblocal.ini", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enb",
            MakeFromArchive("local_enb", "enbseries/enblocal.ini",
                "enbseries/enblocal.ini", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("enbseries/enblocal.ini")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  8. Forward slashes в Destination
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DestinationForwardSlashes_Handled()
    {
        var content = Encoding.UTF8.GetBytes("deep");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/patches/deep.ini", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enb",
            MakeFromArchive("local_enb", "enbseries/patches/deep.ini",
                "enbseries/patches/deep.ini", hash, content.Length));

        await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        File.Exists(StockFile("enbseries/patches/deep.ini")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  9. Entry без директив → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryWithNoDirectives_Skipped()
    {
        var entry = MakeEntry("empty");

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle().Which.Should().Be("empty");
    }

    // ------------------------------------------------------------------
    //  10. Entry только с не-FromArchive директивами → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryWithOnlyNonFromArchiveDirectives_Skipped()
    {
        var entry = MakeEntry("weird",
            new CreateDirectoryDirective { Destination = "enbseries/" });

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle().Which.Should().Be("weird");
    }

    // ------------------------------------------------------------------
    //  11. Две директивы из одного архива
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TwoDirectivesSameArchive_ExtractedOnce()
    {
        var content1 = Encoding.UTF8.GetBytes("one");
        var content2 = Encoding.UTF8.GetBytes("two");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip",
            ("skse64_loader.exe", content1),
            ("skse64_1_6_1170.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse64_loader.exe",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_skse", "skse64_1_6_1170.dll",
                "skse64_1_6_1170.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("skse64_loader.exe")).Should().BeTrue();
        File.Exists(StockFile("skse64_1_6_1170.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  12. Две директивы из разных архивов
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TwoDirectivesDifferentArchives_BothExtracted()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B");

        var archiveA = TestArchives.CreateZip(
            _downloadsPath, "a.zip", ("a.exe", contentA));
        var archiveB = TestArchives.CreateZip(
            _downloadsPath, "b.zip", ("b.dll", contentB));

        var entryA = TestArchives.MakeArchiveEntry(
            archiveA, "local_a", _hashCache);
        var entryB = TestArchives.MakeArchiveEntry(
            archiveB, "local_b", _hashCache);

        var entry = MakeEntry("pair",
            MakeFromArchive("local_a", "a.exe", "a.exe",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_b", "b.dll", "b.dll",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(entryA, entryB)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("a.exe")).Should().BeTrue();
        File.Exists(StockFile("b.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  13. archiveId не найден в map
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveIdNotFound_Throws()
    {
        var content = Encoding.UTF8.GetBytes("x");

        var entry = MakeEntry("skse",
            MakeFromArchive("nonexistent", "skse64_loader.exe",
                "skse64_loader.exe",
                TestArchives.HashOf(content), content.Length));

        var act = async () => await _step.ExecuteAsync(
            MakeInput(new[] { entry }), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonexistent*");
    }

    // ------------------------------------------------------------------
    //  14. Архив в map есть, но файла на диске нет
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveFileMissingOnDisk_Throws()
    {
        var content = Encoding.UTF8.GetBytes("x");

        var fakeEntry = new ArchiveEntry
        {
            Id = "local_fake",
            Name = "fake.zip",
            Size = 100,
            Hash = new XxHash64Value(0xabc),
            Sources = Array.Empty<
                Core.Models.Manifest.Sources.ArchiveSourceRef>(),
        };

        var entry = MakeEntry("skse",
            MakeFromArchive("local_fake", "skse64_loader.exe",
                "skse64_loader.exe",
                TestArchives.HashOf(content), content.Length));

        var act = async () => await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(fakeEntry)),
            CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*fake.zip*");
    }

    // ------------------------------------------------------------------
    //  15. Source не найден в архиве
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_SourceNotInArchive_Throws()
    {
        var content = Encoding.UTF8.GetBytes("present");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip",
            ("skse64_loader.exe", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_1_6_1170.dll",
                "skse64_1_6_1170.dll",
                TestArchives.HashOf(content), content.Length));

        var act = async () => await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*skse64_1_6_1170.dll*");
    }

    // ------------------------------------------------------------------
    //  16. Stock Game/ не существует
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TargetRootMissing_Throws()
    {
        Directory.Delete(_stockGamePath, recursive: true);

        var act = async () => await _step.ExecuteAsync(
            MakeInput(), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage("*Stock Game*");
    }

    // ------------------------------------------------------------------
    //  17. Идемпотентность
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Twice_SecondRunSkipped()
    {
        var content = Encoding.UTF8.GetBytes("idem");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip", ("skse64_loader.exe", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse64_loader.exe", hash, content.Length));

        var input = MakeInput(new[] { entry },
            MakeArchivesById(archiveEntry));

        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        var second = await _step.ExecuteAsync(input, CancellationToken.None);

        first.Written.Should().ContainSingle();
        second.Written.Should().BeEmpty();
        second.Skipped.Should().ContainSingle().Which.Should().Be("skse");
    }

    // ------------------------------------------------------------------
    //  18. Canceled token
    // ------------------------------------------------------------------

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
