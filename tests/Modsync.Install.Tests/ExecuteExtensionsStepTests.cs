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

public class ExecuteExtensionsStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instanceDir;
    private readonly string _mo2Path;
    private readonly string _downloadsPath;
    private readonly FileHashCache _hashCache = new();
    private readonly ExecuteExtensionsStep _step;

    public ExecuteExtensionsStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-ext-" + Guid.NewGuid());
        _instanceDir = Path.Combine(_tempDir, "instance");
        _mo2Path = Path.Combine(_instanceDir, "MO2");
        _downloadsPath = Path.Combine(_mo2Path, "downloads");

        Directory.CreateDirectory(_mo2Path);
        Directory.CreateDirectory(_downloadsPath);
        // Stock Game/ нужен, но этот шаг его не трогает.
        Directory.CreateDirectory(Path.Combine(_instanceDir, "Stock Game"));

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        _step = new ExecuteExtensionsStep(
            extractor,
            NullLogger<ExecuteExtensionsStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private string Mo2File(string relativePath)
        => Path.Combine(_mo2Path,
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

    private ExecuteExtensionsStep.Input MakeInput(
        IReadOnlyList<ExtensionEntry>? entries = null,
        IReadOnlyDictionary<string, ArchiveEntry>? archivesById = null)
    {
        var manifest = MakeManifest(entries ?? Array.Empty<ExtensionEntry>());

        return new ExecuteExtensionsStep.Input
        {
            InstancePath = _instanceDir,
            Manifest = manifest,
            DownloadsPath = _downloadsPath,
            ArchivesById = archivesById
                ?? new Dictionary<string, ArchiveEntry>(StringComparer.Ordinal),
        };
    }

    private static ModlistManifest MakeManifest(
        IReadOnlyList<ExtensionEntry> extensions)
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
                Extensions = extensions,
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
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
        var content = Encoding.UTF8.GetBytes("ext content");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip", ("file.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("plugins/file.dll",
            MakeFromArchive("local_ext", "file.dll", "plugins/file.dll",
                hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("plugins/file.dll");
        output.Skipped.Should().BeEmpty();

        File.Exists(Mo2File("plugins/file.dll")).Should().BeTrue();
        TestArchives.ReadBytes(Mo2File("plugins/file.dll"))
            .Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  3. Entry-файл: файл есть, hash совпадает → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileMatchesHash_Skipped()
    {
        var content = Encoding.UTF8.GetBytes("ext content");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip", ("file.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        // Кладём файл заранее.
        Directory.CreateDirectory(Mo2File("plugins"));
        File.WriteAllBytes(Mo2File("plugins/file.dll"), content);

        var entry = MakeEntry("plugins/file.dll",
            MakeFromArchive("local_ext", "file.dll", "plugins/file.dll",
                hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle()
            .Which.Should().Be("plugins/file.dll");
    }

    // ------------------------------------------------------------------
    //  4. Entry-файл: файл есть, hash не тот → Written, перезаписан
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileWrongHash_OverwrittenAndWritten()
    {
        var content = Encoding.UTF8.GetBytes("ext content");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip", ("file.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        // Кладём с другим содержимым.
        Directory.CreateDirectory(Mo2File("plugins"));
        File.WriteAllBytes(Mo2File("plugins/file.dll"),
            Encoding.UTF8.GetBytes("stale"));

        var entry = MakeEntry("plugins/file.dll",
            MakeFromArchive("local_ext", "file.dll", "plugins/file.dll",
                hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("plugins/file.dll");
        TestArchives.ReadBytes(Mo2File("plugins/file.dll"))
            .Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  5. Entry-папка: часть файлов совпадает → Written
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PackagePartialMatch_OnlyMissingCopied()
    {
        var content1 = Encoding.UTF8.GetBytes("one");
        var content2 = Encoding.UTF8.GetBytes("two");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "pkg.zip",
            ("BethINI/one.dll", content1),
            ("BethINI/two.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        // one.dll уже на месте, two.dll — нет.
        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/one.dll"), content1);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg", "BethINI/one.dll",
                "tools/BethINI/one.dll",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_pkg", "BethINI/two.dll",
                "tools/BethINI/two.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("tools/BethINI");
        File.Exists(Mo2File("tools/BethINI/one.dll")).Should().BeTrue();
        File.Exists(Mo2File("tools/BethINI/two.dll")).Should().BeTrue();
        TestArchives.ReadBytes(Mo2File("tools/BethINI/two.dll"))
            .Should().Equal(content2);
    }

    // ------------------------------------------------------------------
    //  6. Два entries — файл и папка
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TwoEntries_MixedWrittenSkipped()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip",
            ("a.dll", contentA),
            ("B/B.exe", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        // Entry-файл: a.dll уже на месте.
        Directory.CreateDirectory(Mo2File("plugins"));
        File.WriteAllBytes(Mo2File("plugins/a.dll"), contentA);

        var entryA = MakeEntry("plugins/a.dll",
            MakeFromArchive("local_ext", "a.dll", "plugins/a.dll",
                TestArchives.HashOf(contentA), contentA.Length));

        // Entry-папка: B — нет на диске.
        var entryB = MakeEntry("tools/B",
            MakeFromArchive("local_ext", "B/B.exe", "tools/B/B.exe",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entryA, entryB },
                MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("tools/B");
        output.Skipped.Should().ContainSingle()
            .Which.Should().Be("plugins/a.dll");
    }

    // ------------------------------------------------------------------
    //  7. Entry-папка: destination с подпапкой → папки создаются
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PackageWithSubdir_CreatesFolders()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "pkg.zip",
            ("BethINI/sub/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg", "BethINI/sub/data.bin",
                "tools/BethINI/sub/data.bin", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("tools/BethINI/sub/data.bin")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  8. Entry-файл: Destination с подпапкой
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileWithSubdirPath_CreatesParentDir()
    {
        var content = Encoding.UTF8.GetBytes("dll");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip", ("fomod.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("plugins/sub/fomod.dll",
            MakeFromArchive("local_ext", "fomod.dll",
                "plugins/sub/fomod.dll", hash, content.Length));

        await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        File.Exists(Mo2File("plugins/sub/fomod.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  9. Entry без директив → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryWithNoDirectives_Skipped()
    {
        var entry = MakeEntry("plugins/empty.dll");

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle()
            .Which.Should().Be("plugins/empty.dll");
    }

    // ------------------------------------------------------------------
    //  10. Entry только с не-FromArchive директивами → Skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EntryWithOnlyNonFromArchiveDirectives_Skipped()
    {
        var entry = MakeEntry("plugins/weird.dll",
            new CreateDirectoryDirective { Destination = "folder/" });

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }), CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle()
            .Which.Should().Be("plugins/weird.dll");
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
            _downloadsPath, "pkg.zip",
            ("BethINI/one.dll", content1),
            ("BethINI/two.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg", "BethINI/one.dll",
                "tools/BethINI/one.dll",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_pkg", "BethINI/two.dll",
                "tools/BethINI/two.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("tools/BethINI/one.dll")).Should().BeTrue();
        File.Exists(Mo2File("tools/BethINI/two.dll")).Should().BeTrue();
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
            _downloadsPath, "a.zip", ("A/a.dll", contentA));
        var archiveB = TestArchives.CreateZip(
            _downloadsPath, "b.zip", ("B/b.dll", contentB));

        var entryA = TestArchives.MakeArchiveEntry(
            archiveA, "local_a", _hashCache);
        var entryB = TestArchives.MakeArchiveEntry(
            archiveB, "local_b", _hashCache);

        var entry = MakeEntry("tools/Mixed",
            MakeFromArchive("local_a", "A/a.dll",
                "tools/Mixed/a.dll",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_b", "B/b.dll",
                "tools/Mixed/b.dll",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(entryA, entryB)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("tools/Mixed/a.dll")).Should().BeTrue();
        File.Exists(Mo2File("tools/Mixed/b.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  13. ArchiveId не найден в map
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveIdNotFound_Throws()
    {
        var content = Encoding.UTF8.GetBytes("x");

        var entry = MakeEntry("plugins/file.dll",
            MakeFromArchive("nonexistent", "file.dll", "plugins/file.dll",
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

        var entry = MakeEntry("plugins/file.dll",
            MakeFromArchive("local_fake", "file.dll", "plugins/file.dll",
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
            _downloadsPath, "ext.zip", ("present.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("plugins/missing.dll",
            MakeFromArchive("local_ext", "missing.dll",
                "plugins/missing.dll",
                TestArchives.HashOf(content), content.Length));

        var act = async () => await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing.dll*");
    }

    // ------------------------------------------------------------------
    //  16. MO2/ не существует
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TargetRootMissing_Throws()
    {
        Directory.Delete(_mo2Path, recursive: true);

        var act = async () => await _step.ExecuteAsync(
            MakeInput(), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage("*MO2*");
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
            _downloadsPath, "ext.zip", ("file.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("plugins/file.dll",
            MakeFromArchive("local_ext", "file.dll", "plugins/file.dll",
                hash, content.Length));

        var input = MakeInput(new[] { entry },
            MakeArchivesById(archiveEntry));

        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        var second = await _step.ExecuteAsync(input, CancellationToken.None);

        first.Written.Should().ContainSingle();
        second.Written.Should().BeEmpty();
        second.Skipped.Should().ContainSingle()
            .Which.Should().Be("plugins/file.dll");
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
    //  Reconcile: entry — папка (новые тесты из 41.2)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PackageWithExtraFile_Recreated()
    {
        var contentA = Encoding.UTF8.GetBytes("BethINI.exe");
        var contentB = Encoding.UTF8.GetBytes("old-readme.txt");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "bethini.zip",
            ("BethINI/BethINI.exe", contentA));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_bethini", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_bethini",
                "BethINI/BethINI.exe", "tools/BethINI/BethINI.exe",
                TestArchives.HashOf(contentA), contentA.Length));

        // На диске — состояние v1.0.
        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/BethINI.exe"), contentA);
        File.WriteAllBytes(Mo2File("tools/BethINI/old-readme.txt"), contentB);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle()
            .Which.Should().Be("tools/BethINI");
        output.Skipped.Should().BeEmpty();

        File.Exists(Mo2File("tools/BethINI/old-readme.txt")).Should().BeFalse();
        File.Exists(Mo2File("tools/BethINI/BethINI.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_PackageMatchesManifest_Skipped()
    {
        var contentA = Encoding.UTF8.GetBytes("BethINI.exe");
        var contentB = Encoding.UTF8.GetBytes("readme");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "bethini.zip",
            ("BethINI/BethINI.exe", contentA),
            ("BethINI/readme.txt", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_bethini", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_bethini",
                "BethINI/BethINI.exe", "tools/BethINI/BethINI.exe",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_bethini",
                "BethINI/readme.txt", "tools/BethINI/readme.txt",
                TestArchives.HashOf(contentB), contentB.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/BethINI.exe"), contentA);
        File.WriteAllBytes(Mo2File("tools/BethINI/readme.txt"), contentB);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Skipped.Should().ContainSingle()
            .Which.Should().Be("tools/BethINI");
        output.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_PackageWithNestedExtraFile_Recreated()
    {
        var content = Encoding.UTF8.GetBytes("data");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "pkg.zip", ("BethINI/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg",
                "BethINI/data.bin", "tools/BethINI/data.bin",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI/sub"));
        File.WriteAllBytes(Mo2File("tools/BethINI/data.bin"), content);
        File.WriteAllBytes(Mo2File("tools/BethINI/sub/orphan.bin"),
            Encoding.UTF8.GetBytes("orphan"));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("tools/BethINI/sub/orphan.bin")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_EmptySubdirOnDisk_NotRecreated()
    {
        var content = Encoding.UTF8.GetBytes("data");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "pkg.zip", ("BethINI/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg",
                "BethINI/data.bin", "tools/BethINI/data.bin",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI/empty-subdir"));
        File.WriteAllBytes(Mo2File("tools/BethINI/data.bin"), content);

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
            _downloadsPath, "pkg.zip",
            ("BethINI/a.txt", contentA),
            ("BethINI/b.txt", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg",
                "BethINI/a.txt", "tools/BethINI/a.txt",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_pkg",
                "BethINI/b.txt", "tools/BethINI/b.txt",
                TestArchives.HashOf(contentB), contentB.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/a.txt"), contentA);
        // b.txt отсутствует.

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("tools/BethINI/b.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_PackageWithChangedFile_Recreated()
    {
        var expectedContent = Encoding.UTF8.GetBytes("expected");
        var staleContent = Encoding.UTF8.GetBytes("stale!");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "pkg.zip", ("BethINI/a.txt", expectedContent));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg",
                "BethINI/a.txt", "tools/BethINI/a.txt",
                TestArchives.HashOf(expectedContent),
                expectedContent.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/a.txt"), staleContent);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        TestArchives.ReadBytes(Mo2File("tools/BethINI/a.txt"))
            .Should().Equal(expectedContent);
    }

    // ------------------------------------------------------------------
    //  Reconcile: entry — файл (новые тесты из 41.2)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ReconcileFile_MatchesManifest_Skipped()
    {
        var content = Encoding.UTF8.GetBytes("fomod dll");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip", ("fomod.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("plugins/fomod.dll",
            MakeFromArchive("local_ext",
                "fomod.dll", "plugins/fomod.dll",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(Mo2File("plugins"));
        File.WriteAllBytes(Mo2File("plugins/fomod.dll"), content);

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
            _downloadsPath, "ext.zip", ("fomod.dll", expectedContent));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("plugins/fomod.dll",
            MakeFromArchive("local_ext",
                "fomod.dll", "plugins/fomod.dll",
                TestArchives.HashOf(expectedContent),
                expectedContent.Length));

        Directory.CreateDirectory(Mo2File("plugins"));
        File.WriteAllBytes(Mo2File("plugins/fomod.dll"), staleContent);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        TestArchives.ReadBytes(Mo2File("plugins/fomod.dll"))
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
            _downloadsPath, "pkg.zip",
            ("BethINI/data.bin", expectedContent));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg",
                "BethINI/data.bin", "tools/BethINI/data.bin",
                TestArchives.HashOf(expectedContent),
                expectedContent.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/data.bin"), expectedContent);
        File.WriteAllBytes(Mo2File("tools/BethINI/orphan.txt"),
            Encoding.UTF8.GetBytes("orphan"));
        File.WriteAllBytes(Mo2File("tools/other.exe"), siblingContent);

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();

        File.Exists(Mo2File("tools/BethINI/orphan.txt")).Should().BeFalse();

        File.Exists(Mo2File("tools/other.exe")).Should().BeTrue();
        TestArchives.ReadBytes(Mo2File("tools/other.exe"))
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
            _downloadsPath, "pkg.zip", ("BethINI/data.bin", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_pkg", _hashCache);

        var entry = MakeEntry("tools/BethINI",
            MakeFromArchive("local_pkg",
                "BethINI/data.bin", "tools/BethINI/data.bin",
                TestArchives.HashOf(content), content.Length));

        Directory.CreateDirectory(Mo2File("tools/BethINI"));
        File.WriteAllBytes(Mo2File("tools/BethINI/data.bin"), content);
        File.WriteAllBytes(Mo2File("tools/BethINI/orphan.txt"),
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
