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
    //  2. Entry-файл: файла нет → Written
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

        var entry = MakeEntry("skse64_loader.exe",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse64_loader.exe", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("skse64_loader.exe");
        File.Exists(StockFile("skse64_loader.exe")).Should().BeTrue();
        TestArchives.ReadBytes(StockFile("skse64_loader.exe"))
            .Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  3. Entry-файл: файл есть, hash совпадает → Skipped
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

        var entry = MakeEntry("d3d11.dll",
            MakeFromArchive("local_enb", "d3d11.dll",
                "d3d11.dll", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle()
            .Which.Should().Be("d3d11.dll");
    }

    // ------------------------------------------------------------------
    //  4. Entry-файл: файл есть, hash не тот → Written, перезаписан
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

        var entry = MakeEntry("d3d11.dll",
            MakeFromArchive("local_cs", "d3d11.dll",
                "d3d11.dll", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        TestArchives.ReadBytes(StockFile("d3d11.dll")).Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  5. Entry-папка: часть файлов совпадает → Written
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PackagePartialMatch_OnlyMissingCopied()
    {
        var content1 = Encoding.UTF8.GetBytes("skse64_loader.exe");
        var content2 = Encoding.UTF8.GetBytes("skse64_1_6_1170.dll");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip",
            ("skse64_loader.exe", content1),
            ("skse64_1_6_1170.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        // Entry — папка skse с двумя файлами.
        // На диске только loader, dll — нет.
        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse/skse64_loader.exe",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_skse", "skse64_1_6_1170.dll",
                "skse/skse64_1_6_1170.dll",
                TestArchives.HashOf(content2), content2.Length));

        // Кладём только loader.
        Directory.CreateDirectory(StockFile("skse"));
        File.WriteAllBytes(StockFile("skse/skse64_loader.exe"), content1);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("skse");
        File.Exists(StockFile("skse/skse64_loader.exe")).Should().BeTrue();
        File.Exists(StockFile("skse/skse64_1_6_1170.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  6. Два entry — файл и папка
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TwoEntries_MixedWrittenSkipped()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "stuff.zip",
            ("a.exe", contentA),
            ("b/dll.exe", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_stuff", _hashCache);

        // Entry-файл: a.exe уже на месте.
        File.WriteAllBytes(StockFile("a.exe"), contentA);

        var entryA = MakeEntry("a.exe",
            MakeFromArchive("local_stuff", "a.exe", "a.exe",
                TestArchives.HashOf(contentA), contentA.Length));

        // Entry-папка: b — нет на диске.
        var entryB = MakeEntry("b",
            MakeFromArchive("local_stuff", "b/dll.exe", "b/dll.exe",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entryA, entryB },
                MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("b");
        output.Skipped.Should().ContainSingle().Which.Should().Be("a.exe");
    }

    // ------------------------------------------------------------------
    //  7. Entry-папка: destination с подпапкой → папки создаются
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PackageWithSubdir_CreatesFolders()
    {
        var content = Encoding.UTF8.GetBytes("enb settings");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/enblocal.ini", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb", "enbseries/enblocal.ini",
                "enbseries/enblocal.ini", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("enbseries/enblocal.ini")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  8. Entry-файл: Destination с подпапкой
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileWithSubdirPath_CreatesParentDir()
    {
        var content = Encoding.UTF8.GetBytes("deep");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/deep.ini", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries/deep.ini",
            MakeFromArchive("local_enb", "enbseries/deep.ini",
                "enbseries/deep.ini", hash, content.Length));

        await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        File.Exists(StockFile("enbseries/deep.ini")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  9. Entry без директив → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryWithNoDirectives_Skipped()
    {
        var entry = MakeEntry("empty.exe");

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle().Which.Should().Be("empty.exe");
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
    //  11. Entry-папка: две директивы из одного архива
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

        // Entry — папка skse (обе директивы внутри неё).
        var entry = MakeEntry("skse",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse/skse64_loader.exe",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_skse", "skse64_1_6_1170.dll",
                "skse/skse64_1_6_1170.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("skse/skse64_loader.exe")).Should().BeTrue();
        File.Exists(StockFile("skse/skse64_1_6_1170.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  12. Entry-папка: две директивы из разных архивов
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

        var entry = MakeEntry("mixed",
            MakeFromArchive("local_a", "a.exe", "mixed/a.exe",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_b", "b.dll", "mixed/b.dll",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(entryA, entryB)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("mixed/a.exe")).Should().BeTrue();
        File.Exists(StockFile("mixed/b.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  13. ArchiveId не найден в map
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveIdNotFound_Throws()
    {
        var content = Encoding.UTF8.GetBytes("x");

        var entry = MakeEntry("skse64_loader.exe",
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

        var entry = MakeEntry("skse64_loader.exe",
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

        var entry = MakeEntry("skse64_1_6_1170.dll",
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

        var entry = MakeEntry("skse64_loader.exe",
            MakeFromArchive("local_skse", "skse64_loader.exe",
                "skse64_loader.exe", hash, content.Length));

        var input = MakeInput(new[] { entry },
            MakeArchivesById(archiveEntry));

        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        var second = await _step.ExecuteAsync(input, CancellationToken.None);

        first.Written.Should().ContainSingle();
        second.Written.Should().BeEmpty();
        second.Skipped.Should().ContainSingle()
            .Which.Should().Be("skse64_loader.exe");
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

    // ------------------------------------------------------------------
    //  Reconcile: entry — папка
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PackageWithExtraFile_Recreated()
    {
        var contentA = Encoding.UTF8.GetBytes("enblocal");
        var contentB = Encoding.UTF8.GetBytes("old-enb");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/enblocal.ini", contentA));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/enblocal.ini", "enbseries/enblocal.ini",
                TestArchives.HashOf(contentA), contentA.Length));

        // На диске — лишний файл.
        Directory.CreateDirectory(StockFile("enbseries"));
        File.WriteAllBytes(StockFile("enbseries/enblocal.ini"), contentA);
        File.WriteAllBytes(StockFile("enbseries/old-enb.ini"), contentB);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("enbseries");
        output.Skipped.Should().BeEmpty();

        File.Exists(StockFile("enbseries/old-enb.ini")).Should().BeFalse();
        File.Exists(StockFile("enbseries/enblocal.ini")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_PackageMatchesManifest_Skipped()
    {
        var contentA = Encoding.UTF8.GetBytes("enblocal");
        var contentB = Encoding.UTF8.GetBytes("enbseries");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/enblocal.ini", contentA),
            ("enbseries/enbseries.ini", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/enblocal.ini", "enbseries/enblocal.ini",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_enb",
                "enbseries/enbseries.ini", "enbseries/enbseries.ini",
                TestArchives.HashOf(contentB), contentB.Length));

        Directory.CreateDirectory(StockFile("enbseries"));
        File.WriteAllBytes(StockFile("enbseries/enblocal.ini"), contentA);
        File.WriteAllBytes(StockFile("enbseries/enbseries.ini"), contentB);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Skipped.Should().ContainSingle().Which.Should().Be("enbseries");
        output.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_PackageWithNestedExtraFile_Recreated()
    {
        var content = Encoding.UTF8.GetBytes("data");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/data.bin", "enbseries/data.bin",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(StockFile("enbseries/sub"));
        File.WriteAllBytes(StockFile("enbseries/data.bin"), content);
        File.WriteAllBytes(StockFile("enbseries/sub/orphan.bin"),
            Encoding.UTF8.GetBytes("orphan"));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("enbseries/sub/orphan.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_EmptySubdirOnDisk_NotRecreated()
    {
        var content = Encoding.UTF8.GetBytes("data");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/data.bin", "enbseries/data.bin",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(StockFile("enbseries/empty-subdir"));
        File.WriteAllBytes(StockFile("enbseries/data.bin"), content);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Skipped.Should().ContainSingle();
        output.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_PackageWithMissingFile_Recreated()
    {
        var contentA = Encoding.UTF8.GetBytes("a");
        var contentB = Encoding.UTF8.GetBytes("b");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/a.txt", contentA),
            ("enbseries/b.txt", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/a.txt", "enbseries/a.txt",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_enb",
                "enbseries/b.txt", "enbseries/b.txt",
                TestArchives.HashOf(contentB), contentB.Length));

        Directory.CreateDirectory(StockFile("enbseries"));
        File.WriteAllBytes(StockFile("enbseries/a.txt"), contentA);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(StockFile("enbseries/b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_PackageWithChangedFile_Recreated()
    {
        var expectedContent = Encoding.UTF8.GetBytes("expected");
        var staleContent = Encoding.UTF8.GetBytes("stale!");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/a.txt", expectedContent));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/a.txt", "enbseries/a.txt",
                TestArchives.HashOf(expectedContent),
                expectedContent.Length));

        Directory.CreateDirectory(StockFile("enbseries"));
        File.WriteAllBytes(StockFile("enbseries/a.txt"), staleContent);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        TestArchives.ReadBytes(StockFile("enbseries/a.txt"))
            .Should().Equal(expectedContent);
    }

    // ------------------------------------------------------------------
    //  Reconcile: entry — файл
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ReconcileFile_MatchesManifest_Skipped()
    {
        var content = Encoding.UTF8.GetBytes("skse loader");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip", ("skse64_loader.exe", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        var entry = MakeEntry("skse64_loader.exe",
            MakeFromArchive("local_skse",
                "skse64_loader.exe", "skse64_loader.exe",
                TestArchives.HashOf(content), content.Length));

        File.WriteAllBytes(StockFile("skse64_loader.exe"), content);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Skipped.Should().ContainSingle();
        output.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_ReconcileFile_Mismatch_Overwritten()
    {
        var expectedContent = Encoding.UTF8.GetBytes("expected");
        var staleContent = Encoding.UTF8.GetBytes("stale");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "skse.zip", ("skse64_loader.exe", expectedContent));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_skse", _hashCache);

        var entry = MakeEntry("skse64_loader.exe",
            MakeFromArchive("local_skse",
                "skse64_loader.exe", "skse64_loader.exe",
                TestArchives.HashOf(expectedContent),
                expectedContent.Length));

        File.WriteAllBytes(StockFile("skse64_loader.exe"), staleContent);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        TestArchives.ReadBytes(StockFile("skse64_loader.exe"))
            .Should().Equal(expectedContent);
    }

    // ------------------------------------------------------------------
    //  Границы: соседние файлы не трогаются
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_RecreateEntry_DoesNotTouchSiblings()
    {
        var expectedContent = Encoding.UTF8.GetBytes("expected");
        var siblingContent = Encoding.UTF8.GetBytes("sibling");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/data.bin", expectedContent));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/data.bin", "enbseries/data.bin",
                TestArchives.HashOf(expectedContent),
                expectedContent.Length));

        Directory.CreateDirectory(StockFile("enbseries"));
        File.WriteAllBytes(StockFile("enbseries/data.bin"), expectedContent);
        File.WriteAllBytes(StockFile("enbseries/orphan.txt"),
            Encoding.UTF8.GetBytes("orphan"));
        // Соседний файл вне entry — как будто часть игры.
        File.WriteAllBytes(StockFile("SkyrimSE.exe"), siblingContent);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();

        File.Exists(StockFile("enbseries/orphan.txt")).Should().BeFalse();
        File.Exists(StockFile("SkyrimSE.exe")).Should().BeTrue();
        TestArchives.ReadBytes(StockFile("SkyrimSE.exe"))
            .Should().Equal(siblingContent);
    }

    // ------------------------------------------------------------------
    //  Идемпотентность после reconcile
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_SecondRunAfterReconcile_Skipped()
    {
        var content = Encoding.UTF8.GetBytes("data");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "enb.zip",
            ("enbseries/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_enb", _hashCache);

        var entry = MakeEntry("enbseries",
            MakeFromArchive("local_enb",
                "enbseries/data.bin", "enbseries/data.bin",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(StockFile("enbseries"));
        File.WriteAllBytes(StockFile("enbseries/data.bin"), content);
        File.WriteAllBytes(StockFile("enbseries/orphan.txt"),
            Encoding.UTF8.GetBytes("orphan"));

        var input = MakeInput(new[] { entry },
            MakeArchivesById(archiveEntry));

        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        first.Written.Should().ContainSingle();

        var second = await _step.ExecuteAsync(input, CancellationToken.None);
        second.Skipped.Should().ContainSingle();
        second.Written.Should().BeEmpty();
    }
}
