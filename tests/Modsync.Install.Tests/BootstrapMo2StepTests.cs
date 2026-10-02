// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class BootstrapMo2StepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _mo2Path;
    private readonly string _downloadsPath;
    private readonly FileHashCache _hashCache = new();
    private readonly BootstrapMo2Step _step;

    public BootstrapMo2StepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-bmo2-" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "instance");
        _mo2Path = Path.Combine(_instancePath, "MO2");
        _downloadsPath = Path.Combine(_mo2Path, "downloads");

        Directory.CreateDirectory(_instancePath);

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        _step = new BootstrapMo2Step(
            new DownloaderRegistry(Array.Empty<Core.Abstractions.IArchiveDownloader>()),
            _hashCache,
            extractor,
            NullLogger<BootstrapMo2Step>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private (ModlistManifest manifest, byte[] content, XxHash64Value hash)
        PrepareLocalMo2Archive(string name = "mo2.zip",
            params (string path, string content)[] files)
    {
        Directory.CreateDirectory(_downloadsPath);

        var archivePath = Path.Combine(_downloadsPath, name);
        using (var fs = File.Create(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (filePath, fileContent) in files)
            {
                var entry = zip.CreateEntry(filePath);
                using var es = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(fileContent);
                es.Write(bytes, 0, bytes.Length);
            }
        }

        var content = File.ReadAllBytes(archivePath);
        var hash = _hashCache.GetOrCompute(archivePath);

        var manifest = MakeManifest(name, content.Length, hash);

        return (manifest, content, hash);
    }

    private static ModlistManifest MakeManifest(
        string mo2ArchiveName,
        long size,
        XxHash64Value hash,
        ArchiveSourceRef[]? sources = null)
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
                    Id = "local_mod-organizer-2-5-2",
                    Name = mo2ArchiveName,
                    Size = size,
                    Hash = hash,
                    Sources = sources ?? Array.Empty<ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection { Extras = Array.Empty<ExtensionEntry>() },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };
    }

    private BootstrapMo2Step.Input MakeInput(ModlistManifest manifest)
        => new()
        {
            InstancePath = _instancePath,
            Manifest = manifest,
        };

    [Fact]
    public async Task Execute_LocalArchive_ExtractsIntoMo2Folder()
    {
        var (manifest, _, _) = PrepareLocalMo2Archive("mo2.zip",
            ("ModOrganizer.exe", "fake exe"),
            ("plugins/foo.dll", "fake dll"));

        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        File.Exists(Path.Combine(_mo2Path, "ModOrganizer.exe")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_mo2Path, "ModOrganizer.exe"))
            .Should().Be("fake exe");

        File.Exists(Path.Combine(_mo2Path, "plugins", "foo.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_Idempotent_SecondRunSameResult()
    {
        var (manifest, _, _) = PrepareLocalMo2Archive("mo2.zip",
            ("ModOrganizer.exe", "content"),
            ("data/file.txt", "data"));

        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);
        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        File.ReadAllText(Path.Combine(_mo2Path, "ModOrganizer.exe"))
            .Should().Be("content");
        File.ReadAllText(Path.Combine(_mo2Path, "data", "file.txt"))
            .Should().Be("data");
    }

    [Fact]
    public async Task Execute_OverwritesExistingFiles()
    {
        Directory.CreateDirectory(_mo2Path);
        File.WriteAllText(Path.Combine(_mo2Path, "ModOrganizer.exe"), "old");
        Directory.CreateDirectory(Path.Combine(_mo2Path, "data"));
        File.WriteAllText(Path.Combine(_mo2Path, "data", "file.txt"), "old");

        var (manifest, _, _) = PrepareLocalMo2Archive("mo2.zip",
            ("ModOrganizer.exe", "new exe"),
            ("data/file.txt", "new data"));

        await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        File.ReadAllText(Path.Combine(_mo2Path, "ModOrganizer.exe"))
            .Should().Be("new exe");
        File.ReadAllText(Path.Combine(_mo2Path, "data", "file.txt"))
            .Should().Be("new data");
    }

    [Fact]
    public async Task Execute_MissingArchive_DownloadsFromSource()
    {
        var content = FakeMo2Content("fake mo2 download");
        var hash = FakeArchiveDownloader.HashOf(content);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        var source = new MirrorSourceRef
        {
            Url = "https://example.com/mo2.7z",
            Hash = hash,
        };
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(source), content);

        var step = MakeStep(mirrorDownloader);

        var manifest = MakeManifest(
            mo2ArchiveName: "mo2.7z",
            size: content.Length,
            hash: hash,
            sources: new ArchiveSourceRef[] { source });

        await step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        File.Exists(Path.Combine(_downloadsPath, "mo2.7z")).Should().BeTrue();
        File.Exists(Path.Combine(_mo2Path, "ModOrganizer.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_LocalArchiveMismatchHash_DownloadsFresh()
    {
        Directory.CreateDirectory(_downloadsPath);
        File.WriteAllText(Path.Combine(_downloadsPath, "mo2.7z"), "stale");

        var correctContent = FakeMo2Content("correct mo2");
        var correctHash = FakeArchiveDownloader.HashOf(correctContent);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        var source = new MirrorSourceRef
        {
            Url = "https://example.com/mo2.7z",
            Hash = correctHash,
        };
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(source), correctContent);

        var step = MakeStep(mirrorDownloader);
        var manifest = MakeManifest(
            mo2ArchiveName: "mo2.7z",
            size: correctContent.Length,
            hash: correctHash,
            sources: new ArchiveSourceRef[] { source });

        await step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        File.ReadAllBytes(Path.Combine(_downloadsPath, "mo2.7z"))
            .Should().Equal(correctContent);
    }

    [Fact]
    public async Task Execute_ArchiveMissing_NoSources_Throws()
    {
        var manifest = MakeManifest(
            mo2ArchiveName: "missing.7z",
            size: 0,
            hash: new XxHash64Value(0),
            sources: Array.Empty<ArchiveSourceRef>());

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no sources*");
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var (manifest, _, _) = PrepareLocalMo2Archive("mo2.zip",
            ("ModOrganizer.exe", "x"));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(MakeInput(manifest), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static byte[] FakeMo2Content(string marker)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("ModOrganizer.exe");
            using var es = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(marker);
            es.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }

    private BootstrapMo2Step MakeStep(params FakeArchiveDownloader[] downloaders)
    {
        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);
        var registry = new DownloaderRegistry(
            downloaders.Cast<Core.Abstractions.IArchiveDownloader>());

        return new BootstrapMo2Step(
            registry,
            _hashCache,
            extractor,
            NullLogger<BootstrapMo2Step>.Instance);
    }
}
