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
        // Минимальный валидный манифест, чтобы не падало на конструкторе.
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
    //  2. Файла нет → Written
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

        var entry = MakeEntry("ext",
            MakeFromArchive("local_ext", "file.dll", "file.dll",
                hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("ext");
        output.Skipped.Should().BeEmpty();

        File.Exists(Mo2File("file.dll")).Should().BeTrue();
        TestArchives.ReadBytes(Mo2File("file.dll")).Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  3. Файл есть, hash совпадает → Skipped
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
        File.WriteAllBytes(Mo2File("file.dll"), content);

        var entry = MakeEntry("ext",
            MakeFromArchive("local_ext", "file.dll", "file.dll",
                hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().BeEmpty();
        output.Skipped.Should().ContainSingle().Which.Should().Be("ext");
    }

    // ------------------------------------------------------------------
    //  4. Файл есть, hash не тот → Written, файл перезаписан
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
        File.WriteAllBytes(Mo2File("file.dll"),
            Encoding.UTF8.GetBytes("stale"));

        var entry = MakeEntry("ext",
            MakeFromArchive("local_ext", "file.dll", "file.dll",
                hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("ext");
        TestArchives.ReadBytes(Mo2File("file.dll")).Should().Equal(content);
    }

    // ------------------------------------------------------------------
    //  5. Часть файлов совпадает → Written только за недостающие
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PartialMatch_OnlyMissingCopied()
    {
        var content1 = Encoding.UTF8.GetBytes("one");
        var content2 = Encoding.UTF8.GetBytes("two");

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip",
            ("one.dll", content1),
            ("two.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        // one.dll уже на месте, two.dll — нет.
        File.WriteAllBytes(Mo2File("one.dll"), content1);

        var entry = MakeEntry("ext",
            MakeFromArchive("local_ext", "one.dll", "one.dll",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_ext", "two.dll", "two.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("ext");
        File.Exists(Mo2File("one.dll")).Should().BeTrue();
        File.Exists(Mo2File("two.dll")).Should().BeTrue();
        TestArchives.ReadBytes(Mo2File("two.dll")).Should().Equal(content2);
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
            _downloadsPath, "ext.zip",
            ("a.dll", contentA),
            ("b.dll", contentB));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        // A уже на месте, B — нет.
        File.WriteAllBytes(Mo2File("a.dll"), contentA);

        var entryA = MakeEntry("ext-a",
            MakeFromArchive("local_ext", "a.dll", "a.dll",
                TestArchives.HashOf(contentA), contentA.Length));
        var entryB = MakeEntry("ext-b",
            MakeFromArchive("local_ext", "b.dll", "b.dll",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entryA, entryB },
                MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle().Which.Should().Be("ext-b");
        output.Skipped.Should().ContainSingle().Which.Should().Be("ext-a");
    }

    // ------------------------------------------------------------------
    //  7. Destination с подпапкой → папки создаются
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DestinationWithSubdir_CreatesFolders()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var hash = TestArchives.HashOf(content);

        var archivePath = TestArchives.CreateZip(
            _downloadsPath, "ext.zip", ("plugins/fomod.dll", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("fomod",
            MakeFromArchive("local_ext", "plugins/fomod.dll",
                "plugins/fomod.dll", hash, content.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("plugins/fomod.dll")).Should().BeTrue();
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
            _downloadsPath, "ext.zip", ("a/b/c.txt", content));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("deep",
            MakeFromArchive("local_ext", "a/b/c.txt",
                "a/b/c.txt", hash, content.Length));

        await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        File.Exists(Mo2File("a/b/c.txt")).Should().BeTrue();
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
            new CreateDirectoryDirective { Destination = "folder/" });

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
            _downloadsPath, "ext.zip",
            ("one.dll", content1),
            ("two.dll", content2));
        var archiveEntry = TestArchives.MakeArchiveEntry(
            archivePath, "local_ext", _hashCache);

        var entry = MakeEntry("pair",
            MakeFromArchive("local_ext", "one.dll", "one.dll",
                TestArchives.HashOf(content1), content1.Length),
            MakeFromArchive("local_ext", "two.dll", "two.dll",
                TestArchives.HashOf(content2), content2.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(archiveEntry)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("one.dll")).Should().BeTrue();
        File.Exists(Mo2File("two.dll")).Should().BeTrue();
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
            _downloadsPath, "a.zip", ("a.dll", contentA));
        var archiveB = TestArchives.CreateZip(
            _downloadsPath, "b.zip", ("b.dll", contentB));

        var entryA = TestArchives.MakeArchiveEntry(
            archiveA, "local_a", _hashCache);
        var entryB = TestArchives.MakeArchiveEntry(
            archiveB, "local_b", _hashCache);

        var entry = MakeEntry("pair",
            MakeFromArchive("local_a", "a.dll", "a.dll",
                TestArchives.HashOf(contentA), contentA.Length),
            MakeFromArchive("local_b", "b.dll", "b.dll",
                TestArchives.HashOf(contentB), contentB.Length));

        var output = await _step.ExecuteAsync(
            MakeInput(new[] { entry }, MakeArchivesById(entryA, entryB)),
            CancellationToken.None);

        output.Written.Should().ContainSingle();
        File.Exists(Mo2File("a.dll")).Should().BeTrue();
        File.Exists(Mo2File("b.dll")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  13. archiveId не найден в map
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveIdNotFound_Throws()
    {
        var content = Encoding.UTF8.GetBytes("x");

        var entry = MakeEntry("ext",
            MakeFromArchive("nonexistent", "file.dll", "file.dll",
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

        // ArchiveEntry есть, но самого zip-файла нет.
        var fakeEntry = new ArchiveEntry
        {
            Id = "local_fake",
            Name = "fake.zip",
            Size = 100,
            Hash = new XxHash64Value(0xabc),
            Sources = Array.Empty<
                Core.Models.Manifest.Sources.ArchiveSourceRef>(),
        };

        var entry = MakeEntry("ext",
            MakeFromArchive("local_fake", "file.dll", "file.dll",
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

        var entry = MakeEntry("ext",
            MakeFromArchive("local_ext", "missing.dll", "missing.dll",
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

        var entry = MakeEntry("ext",
            MakeFromArchive("local_ext", "file.dll", "file.dll",
                hash, content.Length));

        var input = MakeInput(new[] { entry },
            MakeArchivesById(archiveEntry));

        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        var second = await _step.ExecuteAsync(input, CancellationToken.None);

        first.Written.Should().ContainSingle();
        second.Written.Should().BeEmpty();
        second.Skipped.Should().ContainSingle().Which.Should().Be("ext");
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
