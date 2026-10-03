// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Matching;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class ArchiveMatcherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _downloadsDir;
    private readonly FileHashCache _hashCache = new();

    public ArchiveMatcherTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-am-" + Guid.NewGuid());
        _downloadsDir = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(_downloadsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private string CreateZip(
        string name,
        params (string entryPath, byte[] content)[] files)
    {
        var zipPath = Path.Combine(_downloadsDir, name);
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var (entryPath, content) in files)
        {
            var entry = zip.CreateEntry(entryPath);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        return zipPath;
    }

    private ArchiveEntry MakeArchiveEntry(string id, string name)
        => new()
        {
            Id = id,
            Name = name,
            Size = new FileInfo(Path.Combine(_downloadsDir, name)).Length,
            Hash = _hashCache.GetOrCompute(Path.Combine(_downloadsDir, name)),
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef
                {
                    ModId = 1, FileId = 1, Game = "skyrimspecialedition",
                },
            },
        };

    private async Task<ArchiveMatcher> MakeMatcherAsync(
        params ArchiveEntry[] entries)
    {
        var index = new ArchiveIndex
        {
            Resolved = entries,
            Unresolved = Array.Empty<UnresolvedArchive>(),
        };

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        var matcher = new ArchiveMatcher(
            index, _downloadsDir, extractor, _hashCache,
            NullLogger<ArchiveMatcher>.Instance);

        await matcher.BuildAsync(CancellationToken.None);

        return matcher;
    }

    private static XxHash64Value HashOf(byte[] content)
    {
        using var ms = new MemoryStream(content);
        return XxHash64Value.FromStream(ms);
    }

    private static ScannedFile MakeScannedFile(
        string path, byte[] content) => new()
        {
            RelativePath = path,
            Hash = HashOf(content),
            Size = content.Length,
        };

    // ------------------------------------------------------------------
    //  1. TryMatch до Build → ошибка
    // ------------------------------------------------------------------

    [Fact]
    public void TryMatch_BeforeBuild_Throws()
    {
        var index = new ArchiveIndex
        {
            Resolved = Array.Empty<ArchiveEntry>(),
            Unresolved = Array.Empty<UnresolvedArchive>(),
        };

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        var matcher = new ArchiveMatcher(
            index, _downloadsDir, extractor, _hashCache,
            NullLogger<ArchiveMatcher>.Instance);

        var act = () => matcher.TryMatch(
            MakeScannedFile("file.txt", new byte[] { 1 }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BuildAsync must be called*");
    }

    // ------------------------------------------------------------------
    //  2. Пустой ArchiveIndex — Build, TryMatch → null
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_EmptyIndex_ReturnsNull()
    {
        var matcher = await MakeMatcherAsync();

        var directive = matcher.TryMatch(
            MakeScannedFile("file.txt", new byte[] { 1 }));

        directive.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  3. Точное совпадение (hash, path)
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_ExactMatch_ReturnsDirective()
    {
        var content = new byte[] { 1, 2, 3 };

        CreateZip("test.zip", ("file.txt", content));
        var entry = MakeArchiveEntry("local_test", "test.zip");

        var matcher = await MakeMatcherAsync(entry);

        var directive = matcher.TryMatch(
            MakeScannedFile("file.txt", content));

        directive.Should().NotBeNull();
        directive!.Archive.Should().Be("local_test");
        directive.Source.Should().Be("file.txt");
        directive.Destination.Should().Be("file.txt");
        directive.Size.Should().Be(content.Length);
    }

    // ------------------------------------------------------------------
    //  4. Совпадение по hash, путь отличается
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_ByHashDifferentPath_ReturnsDirective()
    {
        var content = new byte[] { 10, 20, 30 };

        CreateZip("enb.zip", ("enbseries.ini", content));
        var entry = MakeArchiveEntry("local_enb", "enb.zip");

        var matcher = await MakeMatcherAsync(entry);

        var directive = matcher.TryMatch(
            MakeScannedFile("enbseries.ini.mohidden", content));

        directive.Should().NotBeNull();
        directive!.Archive.Should().Be("local_enb");
        directive.Source.Should().Be("enbseries.ini");
        directive.Destination.Should().Be("enbseries.ini.mohidden");
    }

    // ------------------------------------------------------------------
    //  5. Exact wins over ByHash
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_ExactWinsOverByHash()
    {
        var content = new byte[] { 100, 101, 102 };

        CreateZip("a-exact.zip", ("enbseries.ini", content));
        CreateZip("z-byhash.zip", ("enbseries.ini.mohidden", content));

        var exact = MakeArchiveEntry(
            "nexus_skyrimspecialedition_1_1", "a-exact.zip");
        var byHash = MakeArchiveEntry(
            "nexus_skyrimspecialedition_2_2", "z-byhash.zip");

        var matcher = await MakeMatcherAsync(exact, byHash);

        var directive = matcher.TryMatch(
            MakeScannedFile("enbseries.ini", content));

        directive.Should().NotBeNull();
        directive!.Archive.Should().Be("nexus_skyrimspecialedition_1_1");
        directive.Source.Should().Be("enbseries.ini");
    }

    // ------------------------------------------------------------------
    //  6. Duplicate hash — детерминизм по алфавиту archiveId
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_DuplicateHashInMultipleArchives_Deterministic()
    {
        var content = new byte[] { 5, 5, 5 };

        CreateZip("second.zip", ("file.txt", content));
        CreateZip("first.zip", ("file.txt", content));

        var second = MakeArchiveEntry(
            "nexus_skyrimspecialedition_2_2", "second.zip");
        var first = MakeArchiveEntry(
            "nexus_skyrimspecialedition_1_1", "first.zip");

        var matcher = await MakeMatcherAsync(second, first);

        var directive = matcher.TryMatch(
            MakeScannedFile("file.txt", content));

        directive.Should().NotBeNull();
        directive!.Archive.Should().Be("nexus_skyrimspecialedition_1_1");
    }

    // ------------------------------------------------------------------
    //  7. Файл не найден ни по hash, ни по path
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_NoMatch_ReturnsNull()
    {
        var content = new byte[] { 1 };
        CreateZip("test.zip", ("file.txt", content));
        var entry = MakeArchiveEntry("local_test", "test.zip");

        var matcher = await MakeMatcherAsync(entry);

        var directive = matcher.TryMatch(
            MakeScannedFile("missing.txt", new byte[] { 99 }));

        directive.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  8. Несколько файлов в одном архиве
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_MultipleFilesInOneArchive_AllMatched()
    {
        var content1 = new byte[] { 1, 1, 1 };
        var content2 = new byte[] { 2, 2, 2 };

        CreateZip("multi.zip",
            ("a.txt", content1),
            ("b.txt", content2));

        var entry = MakeArchiveEntry("local_multi", "multi.zip");

        var matcher = await MakeMatcherAsync(entry);

        var d1 = matcher.TryMatch(MakeScannedFile("a.txt", content1));
        var d2 = matcher.TryMatch(MakeScannedFile("b.txt", content2));

        d1.Should().NotBeNull();
        d1!.Source.Should().Be("a.txt");
        d2.Should().NotBeNull();
        d2!.Source.Should().Be("b.txt");
    }

    // ------------------------------------------------------------------
    //  9. Файлы в разных архивах
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_TwoArchives_TwoMatched()
    {
        var contentA = new byte[] { 1 };
        var contentB = new byte[] { 2 };

        CreateZip("a.zip", ("a.txt", contentA));
        CreateZip("b.zip", ("b.txt", contentB));

        var entryA = MakeArchiveEntry("local_a", "a.zip");
        var entryB = MakeArchiveEntry("local_b", "b.zip");

        var matcher = await MakeMatcherAsync(entryA, entryB);

        var dA = matcher.TryMatch(MakeScannedFile("a.txt", contentA));
        var dB = matcher.TryMatch(MakeScannedFile("b.txt", contentB));

        dA!.Archive.Should().Be("local_a");
        dB!.Archive.Should().Be("local_b");
    }

    // ------------------------------------------------------------------
    //  10. .mohidden в Destination
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_MohiddenDestination_Preserved()
    {
        var content = new byte[] { 7, 7, 7 };
        CreateZip("meshes.zip", ("meshes/file.nif", content));
        var entry = MakeArchiveEntry("local_meshes", "meshes.zip");

        var matcher = await MakeMatcherAsync(entry);

        var directive = matcher.TryMatch(
            MakeScannedFile("meshes.mohidden/file.nif", content));

        directive.Should().NotBeNull();
        directive!.Source.Should().Be("meshes/file.nif");
        directive.Destination.Should().Be("meshes.mohidden/file.nif");
    }

    // ------------------------------------------------------------------
    //  11. Build идемпотентен (повторный вызов — no-op)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_Twice_NoOp()
    {
        var content = new byte[] { 1 };
        CreateZip("test.zip", ("file.txt", content));
        var entry = MakeArchiveEntry("local_test", "test.zip");

        var matcher = await MakeMatcherAsync(entry);

        var act = async () => await matcher.BuildAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        var directive = matcher.TryMatch(
            MakeScannedFile("file.txt", content));
        directive.Should().NotBeNull();
    }

    // ------------------------------------------------------------------
    //  12. Пустой архив — Build, TryMatch → null
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_EmptyArchive_ReturnsNull()
    {
        CreateZip("empty.zip");
        var entry = MakeArchiveEntry("local_empty", "empty.zip");

        var matcher = await MakeMatcherAsync(entry);

        var directive = matcher.TryMatch(
            MakeScannedFile("file.txt", new byte[] { 1 }));

        directive.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  13. Hash в директиве совпадает с файлом
    // ------------------------------------------------------------------

    [Fact]
    public async Task TryMatch_DirectiveHash_MatchesFileHash()
    {
        var content = new byte[] { 42, 43, 44 };
        CreateZip("test.zip", ("file.bin", content));
        var entry = MakeArchiveEntry("local_test", "test.zip");

        var matcher = await MakeMatcherAsync(entry);

        var file = MakeScannedFile("file.bin", content);
        var directive = matcher.TryMatch(file);

        directive.Should().NotBeNull();
        directive!.Hash.Should().Be(file.Hash);
        directive.Size.Should().Be(file.Size);
    }

    // ------------------------------------------------------------------
    //  14. Архив с несколькими файлами — Build не падает
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_ArchiveWithManyFiles_Succeeds()
    {
        var files = Enumerable.Range(0, 20)
            .Select(i => ($"file{i:D2}.txt", new byte[] { (byte)i }))
            .ToArray();

        CreateZip("many.zip", files);
        var entry = MakeArchiveEntry("local_many", "many.zip");

        var matcher = await MakeMatcherAsync(entry);

        for (int i = 0; i < 20; i++)
        {
            var d = matcher.TryMatch(
                MakeScannedFile($"file{i:D2}.txt", new byte[] { (byte)i }));
            d.Should().NotBeNull();
        }
    }

    // ------------------------------------------------------------------
    //  15. Canceled token в Build → throw
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_CanceledToken_Throws()
    {
        var content = new byte[] { 1 };
        CreateZip("test.zip", ("file.txt", content));
        var entry = MakeArchiveEntry("local_test", "test.zip");

        var index = new ArchiveIndex
        {
            Resolved = new[] { entry },
            Unresolved = Array.Empty<UnresolvedArchive>(),
        };

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        var matcher = new ArchiveMatcher(
            index, _downloadsDir, extractor, _hashCache,
            NullLogger<ArchiveMatcher>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await matcher.BuildAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task BuildAsync_WithProgress_ReportsIncrementingCounter()
    {
        var content1 = new byte[] { 1 };
        var content2 = new byte[] { 2 };
        var content3 = new byte[] { 3 };

        CreateZip("a.zip", ("file1.txt", content1));
        CreateZip("b.zip", ("file2.txt", content2));
        CreateZip("c.zip", ("file3.txt", content3));

        var e1 = MakeArchiveEntry("a", "a.zip");
        var e2 = MakeArchiveEntry("b", "b.zip");
        var e3 = MakeArchiveEntry("c", "c.zip");

        var index = new ArchiveIndex
        {
            Resolved = new[] { e1, e2, e3 },
            Unresolved = Array.Empty<UnresolvedArchive>(),
        };

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        var matcher = new ArchiveMatcher(
            index, _downloadsDir, extractor, _hashCache,
            NullLogger<ArchiveMatcher>.Instance);

        var reports = new List<(int Processed, int Total)>();
        var progress = new Progress<(int, int)>(p => reports.Add(p));

        await matcher.BuildAsync(CancellationToken.None, progress);

        // Ожидаем: стартовый (0, 3), затем 3 репорта после каждого архива.
        // Progress<T> в BCL кеширует в SynchronizationContext — в тесте
        // его нет, поэтому вызовы идут синхронно в том же потоке.
        reports.Should().HaveCount(4);
        reports[0].Should().Be((0, 3));
        reports[^1].Should().Be((3, 3));

        var processedValues = reports.Select(r => r.Processed).Distinct().OrderBy(x => x).ToList();
        processedValues.Should().Equal(0, 1, 2, 3);
    }

}
