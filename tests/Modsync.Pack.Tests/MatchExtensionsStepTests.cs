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

public class MatchExtensionsStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _downloadsDir;
    private readonly FileHashCache _hashCache = new();
    private readonly MatchExtensionsStep _step;

    public MatchExtensionsStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-me-" + Guid.NewGuid());
        _downloadsDir = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(_downloadsDir);

        _step = new MatchExtensionsStep(
            NullLogger<MatchExtensionsStep>.Instance);
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
            new MatchExtensionsStep.Input
            {
                Scan = MakeScan(),
                Matcher = matcher,
            }, CancellationToken.None);

        result.Directives.Should().BeEmpty();
        result.Unmatched.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Exact match
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileInArchive_ProducesDirective()
    {
        var content = Encoding.UTF8.GetBytes("fomod dll content");

        CreateZip("mo2.zip", ("plugins/fomod_plus_installer.dll", content));
        var archiveEntry = MakeArchiveEntry("local_mo2", "mo2.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("plugins/fomod_plus_installer.dll", new[]
            {
                MakeScannedFile("plugins/fomod_plus_installer.dll", content),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtensionsStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives.Should().ContainKey("plugins/fomod_plus_installer.dll");
        var directives = result.Directives["plugins/fomod_plus_installer.dll"];
        directives.Should().HaveCount(1);

        var d = directives[0].Should().BeOfType<FromArchiveDirective>().Subject;
        d.Archive.Should().Be("local_mo2");
        d.Source.Should().Be("plugins/fomod_plus_installer.dll");
        d.Destination.Should().Be("plugins/fomod_plus_installer.dll");

        result.Unmatched.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Директория с несколькими файлами
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DirectoryWithMultipleFiles_AllMatched()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B");

        CreateZip("mo2.zip",
            ("tools/BethINI/BethINI.exe", contentA),
            ("tools/BethINI/readme.txt", contentB));

        var archiveEntry = MakeArchiveEntry("local_mo2", "mo2.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("tools/BethINI", new[]
            {
                MakeScannedFile("tools/BethINI/BethINI.exe", contentA),
                MakeScannedFile("tools/BethINI/readme.txt", contentB),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtensionsStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives["tools/BethINI"].Should().HaveCount(2);
        result.Unmatched.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Match by hash, different path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ByHashDifferentPath_ProducesDirective()
    {
        var content = Encoding.UTF8.GetBytes("some bytes");

        CreateZip("mo2.zip", ("plugins/fomod.dll", content));
        var archiveEntry = MakeArchiveEntry("local_mo2", "mo2.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("plugins/fomod_renamed.dll", new[]
            {
                MakeScannedFile("plugins/fomod_renamed.dll", content),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtensionsStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        var d = result.Directives["plugins/fomod_renamed.dll"][0]
            .Should().BeOfType<FromArchiveDirective>().Subject;
        d.Source.Should().Be("plugins/fomod.dll");
        d.Destination.Should().Be("plugins/fomod_renamed.dll");
    }

    // ------------------------------------------------------------------
    //  Unmatched
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileNotInAnyArchive_GoesToUnmatched()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B-orphan");

        CreateZip("mo2.zip", ("plugins/fomod.dll", contentA));
        var archiveEntry = MakeArchiveEntry("local_mo2", "mo2.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("plugins/fomod.dll", new[]
            {
                MakeScannedFile("plugins/fomod.dll", contentA),
                MakeScannedFile("plugins/extra.bin", contentB),
            }));

        var result = await _step.ExecuteAsync(
            new MatchExtensionsStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives["plugins/fomod.dll"].Should().HaveCount(1);
        result.Unmatched.Should().HaveCount(1);
        result.Unmatched[0].EntryName.Should().Be("plugins/fomod.dll");
        result.Unmatched[0].RelativePath.Should().Be("plugins/extra.bin");
        result.Unmatched[0].Size.Should().Be(contentB.Length);
    }

    // ------------------------------------------------------------------
    //  Несколько entries
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TwoEntries_BothMatchedSeparately()
    {
        var contentA = Encoding.UTF8.GetBytes("A");
        var contentB = Encoding.UTF8.GetBytes("B");

        CreateZip("mo2.zip",
            ("plugins/a.dll", contentA),
            ("tools/B/B.exe", contentB));

        var archiveEntry = MakeArchiveEntry("local_mo2", "mo2.zip");
        var matcher = await MakeMatcherAsync(archiveEntry);

        var scan = MakeScan(
            ("plugins/a.dll", new[] { MakeScannedFile("plugins/a.dll", contentA) }),
            ("tools/B", new[] { MakeScannedFile("tools/B/B.exe", contentB) }));

        var result = await _step.ExecuteAsync(
            new MatchExtensionsStep.Input { Scan = scan, Matcher = matcher },
            CancellationToken.None);

        result.Directives.Should().HaveCount(2);
        result.Directives["plugins/a.dll"].Should().HaveCount(1);
        result.Directives["tools/B"].Should().HaveCount(1);
        result.Unmatched.Should().BeEmpty();
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
            new MatchExtensionsStep.Input
            {
                Scan = MakeScan(),
                Matcher = matcher,
            }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
