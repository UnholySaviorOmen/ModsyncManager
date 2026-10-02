// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Pack.Matching;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Pack.Tests;

public class MatchExtrasStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _downloadsDir;
    private readonly FileHashCache _hashCache = new();
    private readonly MatchExtrasStep _step;

    public MatchExtrasStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-mx-" + Guid.NewGuid());
        _downloadsDir = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(_downloadsDir);

        _step = new MatchExtrasStep(
            NullLogger<MatchExtrasStep>.Instance);
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
                new MirrorSourceRef
                {
                    Url = "https://example.com/" + name,
                    Hash = new XxHash64Value(0xabc),
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

    private static EntryScanResult MakeScan(
        params (string entryName, ScannedFile[] files)[] entries)
    {
        var dict = new Dictionary<string, IReadOnlyList<ScannedFile>>(
            StringComparer.Ordinal);

        foreach (var (name, files) in entries)
            dict[name] = files;

        return new EntryScanResult { Entries = dict };
    }

    private static ScannedFile MakeScannedFile(string path, byte[] content)
    {
        using var ms = new MemoryStream(content);
        return new ScannedFile
        {
            RelativePath = path,
            Hash = XxHash64Value.FromStream(ms),
            Size = content.Length,
        };
    }

    // ------------------------------------------------------------------
    //  Пустой Scan
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyScan_ReturnsEmptyResult()
    {
        var matcher = await MakeMatcherAsync();

        var result = await _step.ExecuteAsync(
            new MatchExtrasStep.Input
            {
                Scan = MakeScan(),
                Matcher = matcher,
            }, CancellationToken.None);

        result.Directives.Should().BeEmpty();
        result.Unmatched.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Exact match: файл SKSE
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileInArchive_ProducesDirective()
    {
        var content = Encoding.UTF8.GetBytes("skse64 loader");

        CreateZip("skse.zip", ("skse64_loader.exe", content));
        var archiveEntry = MakeArchiveEntry("local_skse", "skse.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("skse64_loader.exe", new[]
            {
                MakeScannedFile("skse64_loader.exe", content),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtrasStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives.Should().ContainKey("skse64_loader.exe");
        var directives = result.Directives["skse64_loader.exe"];
        directives.Should().HaveCount(1);

        var d = directives[0].Should().BeOfType<FromArchiveDirective>().Subject;
        d.Archive.Should().Be("local_skse");
        d.Source.Should().Be("skse64_loader.exe");
        d.Destination.Should().Be("skse64_loader.exe");

        result.Unmatched.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Директория enbseries
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DirectoryWithMultipleFiles_AllMatched()
    {
        var contentA = Encoding.UTF8.GetBytes("enblocal");
        var contentB = Encoding.UTF8.GetBytes("enbseries");

        CreateZip("enb.zip",
            ("enbseries/enblocal.ini", contentA),
            ("enbseries/enbseries.ini", contentB));

        var archiveEntry = MakeArchiveEntry("local_enb", "enb.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("enbseries", new[]
            {
                MakeScannedFile("enbseries/enblocal.ini", contentA),
                MakeScannedFile("enbseries/enbseries.ini", contentB),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtrasStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives["enbseries"].Should().HaveCount(2);
        result.Unmatched.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Match by hash, different path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ByHashDifferentPath_ProducesDirective()
    {
        var content = Encoding.UTF8.GetBytes("same content");

        CreateZip("enb.zip", ("enbseries/d3d11.dll", content));
        var archiveEntry = MakeArchiveEntry("local_enb", "enb.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("d3d11.dll", new[]
            {
                MakeScannedFile("d3d11.dll", content),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtrasStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        var d = result.Directives["d3d11.dll"][0]
            .Should().BeOfType<FromArchiveDirective>().Subject;
        d.Source.Should().Be("enbseries/d3d11.dll");
        d.Destination.Should().Be("d3d11.dll");
    }

    // ------------------------------------------------------------------
    //  Unmatched
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileNotInAnyArchive_GoesToUnmatched()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B-orphan");

        CreateZip("skse.zip", ("skse64_loader.exe", contentA));
        var archiveEntry = MakeArchiveEntry("local_skse", "skse.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("skse64_loader.exe", new[]
            {
                MakeScannedFile("skse64_loader.exe", contentA),
                MakeScannedFile("extra-file.bin", contentB),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtrasStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives["skse64_loader.exe"].Should().HaveCount(1);
        result.Unmatched.Should().HaveCount(1);
        result.Unmatched[0].EntryName.Should().Be("skse64_loader.exe");
        result.Unmatched[0].RelativePath.Should().Be("extra-file.bin");
    }

    // ------------------------------------------------------------------
    //  Canceled token
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var matcher = await MakeMatcherAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            new MatchExtrasStep.Input
            {
                Scan = MakeScan(),
                Matcher = matcher,
            }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
