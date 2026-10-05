// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class SyncArchivesStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _downloadsPath;
    private readonly FileHashCache _hashCache = new();

    public SyncArchivesStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-sa-" + Guid.NewGuid());
        _downloadsPath = Path.Combine(_tempDir, "downloads");
        Directory.CreateDirectory(_downloadsPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private SyncArchivesStep MakeStep(params FakeArchiveDownloader[] downloaders)
    {
        var registry = new DownloaderRegistry(
            downloaders.Cast<Core.Abstractions.IArchiveDownloader>());
        return new SyncArchivesStep(
            registry, _hashCache, NullLogger<SyncArchivesStep>.Instance);
    }

    private static ModlistManifest MakeManifest(params ArchiveEntry[] archives)
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
                    Sources = Array.Empty<ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection { Extras = Array.Empty<ExtensionEntry>() },
            Archives = archives,
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };
    }

    private static ArchiveEntry MakeMirrorArchive(
        string name, byte[] content, string url = "https://example.com/file.7z")
    {
        var hash = FakeArchiveDownloader.HashOf(content);
        return new ArchiveEntry
        {
            Id = $"local_{name}",
            Name = name,
            Size = content.Length,
            Hash = hash,
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef { Url = url, Hash = hash },
            },
        };
    }

    private static SyncArchivesStep.Input MakeInput(
        ModlistManifest manifest,
        string downloadsPath,
        int maxParallel = 4,
        IProgress<(int Completed, int Total)>? detailProgress = null)
        => new()
        {
            Manifest = manifest,
            DownloadsPath = downloadsPath,
            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
            DetailProgress = detailProgress,
        };

    /// <summary>
    /// Синхронный IProgress, копит репорты в список.
    ///
    /// DetailProgress в SyncArchivesStep вызывается из параллельных
    /// воркеров Parallel.ForEachAsync. List&lt;T&gt; не потокобезопасен,
    /// поэтому Add обёрнут в lock. Без этого возможна потеря репортов
    /// (например, {0,1,2,3,5} вместо {0,1,2,3,4,5}).
    /// </summary>
    private sealed class ListProgress : IProgress<(int Completed, int Total)>
    {
        private readonly object _lock = new();
        public List<(int Completed, int Total)> Reports { get; } = new();

        public void Report((int Completed, int Total) value)
        {
            lock (_lock)
            {
                Reports.Add(value);
            }
        }
    }

    // ------------------------------------------------------------------
    //  Старые тесты — без изменений
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ArchiveAlreadyInDownloads_IsAlreadyPresent()
    {
        var content = FakeArchiveDownloader.MakeBytes("fake archive");
        var archive = MakeMirrorArchive("Test.7z", content);

        File.WriteAllBytes(Path.Combine(_downloadsPath, "Test.7z"), content);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        var step = MakeStep(mirrorDownloader);

        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        output.AlreadyPresent.Should().ContainSingle().Which.Should().Be("Test.7z");
        output.Downloaded.Should().BeEmpty();
        mirrorDownloader.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_ArchiveWithDifferentNameButSameHash_IsAlreadyPresent()
    {
        var content = FakeArchiveDownloader.MakeBytes("fake archive");
        var archive = MakeMirrorArchive("Expected.7z", content);

        File.WriteAllBytes(Path.Combine(_downloadsPath, "Different.7z"), content);

        var step = MakeStep(new FakeArchiveDownloader("mirror"));
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        output.AlreadyPresent.Should().ContainSingle().Which.Should().Be("Expected.7z");
    }

    [Fact]
    public async Task Execute_MissingMirrorArchive_Downloads()
    {
        var content = FakeArchiveDownloader.MakeBytes("mirror content");
        var archive = MakeMirrorArchive("M.7z", content);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);

        var step = MakeStep(mirrorDownloader);
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        output.Downloaded.Should().ContainSingle().Which.Should().Be("M.7z");
    }

    [Fact]
    public async Task Execute_MultipleArchives_ProcessedInParallel()
    {
        var archives = new List<ArchiveEntry>();
        var mirrorDownloader = new FakeArchiveDownloader("mirror");

        for (int i = 0; i < 5; i++)
        {
            var content = FakeArchiveDownloader.MakeBytes($"content-{i}");
            var archive = MakeMirrorArchive($"File{i}.7z", content,
                url: $"https://example.com/file{i}.7z");
            archives.Add(archive);
            mirrorDownloader.SetContent(
                FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);
        }

        var step = MakeStep(mirrorDownloader);
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archives.ToArray()), _downloadsPath),
            CancellationToken.None);

        output.Downloaded.Should().HaveCount(5);
        output.AlreadyPresent.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NexusSourceWithoutDownloader_SkipsWithWarning()
    {
        var content = FakeArchiveDownloader.MakeBytes("nexus content");
        var hash = FakeArchiveDownloader.HashOf(content);
        var archive = new ArchiveEntry
        {
            Id = "nexus_skyrimspecialedition_1_1",
            Name = "Nexus.7z",
            Size = content.Length,
            Hash = hash,
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef { Game = "skyrimspecialedition", ModId = 1, FileId = 1 },
            },
        };

        var step = MakeStep(new FakeArchiveDownloader("mirror"));

        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Nexus.7z*");
    }

    [Fact]
    public async Task Execute_NexusPlusMirrorSource_FallsBackToMirror()
    {
        var content = FakeArchiveDownloader.MakeBytes("dual content");
        var hash = FakeArchiveDownloader.HashOf(content);

        var archive = new ArchiveEntry
        {
            Id = "dual",
            Name = "Dual.7z",
            Size = content.Length,
            Hash = hash,
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef { Game = "skyrimspecialedition", ModId = 1, FileId = 1 },
                new MirrorSourceRef { Url = "https://example.com/file.7z", Hash = hash },
            },
        };

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent("mirror:https://example.com/file.7z", content);

        var step = MakeStep(mirrorDownloader);
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        output.Downloaded.Should().ContainSingle().Which.Should().Be("Dual.7z");
    }

    [Fact]
    public async Task Execute_NoDownloaderForOnlySource_Throws()
    {
        var content = FakeArchiveDownloader.MakeBytes("x");
        var hash = FakeArchiveDownloader.HashOf(content);
        var archive = new ArchiveEntry
        {
            Id = "nexus_1_1",
            Name = "N.7z",
            Size = content.Length,
            Hash = hash,
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef { Game = "g", ModId = 1, FileId = 1 },
            },
        };

        var step = MakeStep();
        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*N.7z*");
    }

    [Fact]
    public async Task Execute_HashMismatchAfterDownload_Throws()
    {
        var expectedContent = FakeArchiveDownloader.MakeBytes("expected");
        var wrongContent = FakeArchiveDownloader.MakeBytes("wrong!");

        var archive = MakeMirrorArchive("Hash.7z", expectedContent);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(archive.Sources[0]),
            wrongContent);

        var step = MakeStep(mirrorDownloader);

        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Hash.7z*");
    }

    [Fact]
    public async Task Execute_ArchiveWithNoSources_Throws()
    {
        var archive = new ArchiveEntry
        {
            Id = "no-sources",
            Name = "NoSources.7z",
            Size = 100,
            Hash = new XxHash64Value(0xabc),
            Sources = Array.Empty<ArchiveSourceRef>(),
        };

        var step = MakeStep(new FakeArchiveDownloader("mirror"));
        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NoSources.7z*");
    }

    [Fact]
    public async Task Execute_RetryAfterTransientFailure_Succeeds()
    {
        var content = FakeArchiveDownloader.MakeBytes("retry content");
        var archive = MakeMirrorArchive("Retry.7z", content);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);
        mirrorDownloader.FailOnCall(1);

        var step = MakeStep(mirrorDownloader);
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        output.Downloaded.Should().ContainSingle().Which.Should().Be("Retry.7z");
        mirrorDownloader.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Execute_FailedDownload_LeavesNoPartFile()
    {
        var content = FakeArchiveDownloader.MakeBytes("x");
        var wrongContent = FakeArchiveDownloader.MakeBytes("y");
        var archive = MakeMirrorArchive("Failed.7z", content);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), wrongContent);

        var step = MakeStep(mirrorDownloader);
        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();

        Directory.GetFiles(_downloadsPath, "*.part", SearchOption.TopDirectoryOnly)
            .Should().BeEmpty();

        File.Exists(Path.Combine(_downloadsPath, "Failed.7z")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_IgnoresPartAndMetaFiles()
    {
        var content = FakeArchiveDownloader.MakeBytes("x");
        var archive = MakeMirrorArchive("Real.7z", content);

        File.WriteAllText(Path.Combine(_downloadsPath, "garbage.part"), "junk");
        File.WriteAllText(Path.Combine(_downloadsPath, "some.meta"), "junk");
        File.WriteAllText(Path.Combine(_downloadsPath, "readme.txt"), "junk");

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);

        var step = MakeStep(mirrorDownloader);
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        output.Downloaded.Should().ContainSingle().Which.Should().Be("Real.7z");
    }

    [Fact]
    public async Task Execute_EmptyManifest_ReturnsEmptyOutput()
    {
        var step = MakeStep();
        var output = await step.ExecuteAsync(
            MakeInput(MakeManifest(), _downloadsPath),
            CancellationToken.None);

        output.AlreadyPresent.Should().BeEmpty();
        output.Downloaded.Should().BeEmpty();
        output.Skipped.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Новые тесты — DetailProgress
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DetailProgress_ReportsStartAndFinish()
    {
        var archives = new List<ArchiveEntry>();
        var mirrorDownloader = new FakeArchiveDownloader("mirror");

        for (int i = 0; i < 3; i++)
        {
            var content = FakeArchiveDownloader.MakeBytes($"content-{i}");
            var archive = MakeMirrorArchive($"File{i}.7z", content,
                url: $"https://example.com/file{i}.7z");
            archives.Add(archive);
            mirrorDownloader.SetContent(
                FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);
        }

        var progress = new ListProgress();

        var step = MakeStep(mirrorDownloader);
        await step.ExecuteAsync(
            MakeInput(MakeManifest(archives.ToArray()), _downloadsPath,
                detailProgress: progress),
            CancellationToken.None);

        // Как минимум: стартовый (0, 3) и финальный (3, 3).
        progress.Reports.Should().Contain((0, 3));
        progress.Reports.Should().Contain((3, 3));

        // Финальный репорт — последний.
        progress.Reports.Last().Should().Be((3, 3));
    }

    [Fact]
    public async Task Execute_DetailProgress_ReportsIncrementingCounter()
    {
        var archives = new List<ArchiveEntry>();
        var mirrorDownloader = new FakeArchiveDownloader("mirror");

        for (int i = 0; i < 5; i++)
        {
            var content = FakeArchiveDownloader.MakeBytes($"content-{i}");
            var archive = MakeMirrorArchive($"File{i}.7z", content,
                url: $"https://example.com/file{i}.7z");
            archives.Add(archive);
            mirrorDownloader.SetContent(
                FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);
        }

        var progress = new ListProgress();

        var step = MakeStep(mirrorDownloader);
        await step.ExecuteAsync(
            MakeInput(MakeManifest(archives.ToArray()), _downloadsPath,
                detailProgress: progress),
            CancellationToken.None);

        // Все Completed от 0 до 5 должны встретиться.
        var completedValues = progress.Reports
            .Select(r => r.Completed)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        completedValues.Should().Equal(0, 1, 2, 3, 4, 5);

        // Total всегда = 5.
        progress.Reports.Should().AllSatisfy(r => r.Total.Should().Be(5));
    }

    [Fact]
    public async Task Execute_WithoutDetailProgress_DoesNotThrow()
    {
        var content = FakeArchiveDownloader.MakeBytes("x");
        var archive = MakeMirrorArchive("Test.7z", content);

        var mirrorDownloader = new FakeArchiveDownloader("mirror");
        mirrorDownloader.SetContent(
            FakeArchiveDownloader.IdentifierOf(archive.Sources[0]), content);

        var step = MakeStep(mirrorDownloader);

        // DetailProgress = null (по умолчанию в MakeInput).
        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Execute_EmptyManifest_StillReportsStartAndFinish()
    {
        var progress = new ListProgress();
        var step = MakeStep();

        await step.ExecuteAsync(
            MakeInput(MakeManifest(), _downloadsPath, detailProgress: progress),
            CancellationToken.None);

        // 0 / 0 стартовый, 0 / 0 финальный.
        progress.Reports.Should().HaveCount(2);
        progress.Reports[0].Should().Be((0, 0));
        progress.Reports[1].Should().Be((0, 0));
    }

    [Fact]
    public async Task Execute_NexusAuthFailure_ThrowsHelpfulMessage()
    {
        var content = FakeArchiveDownloader.MakeBytes("x");
        var hash = FakeArchiveDownloader.HashOf(content);

        var archive = new ArchiveEntry
        {
            Id = "nexus_skyrimspecialedition_1_1",
            Name = "N.7z",
            Size = content.Length,
            Hash = hash,
            Sources = new ArchiveSourceRef[]
            {
                new NexusSourceRef
                {
                    Game = "skyrimspecialedition",
                    ModId = 1,
                    FileId = 1,
                },
            },
        };

        var nexusDownloader = new FakeArchiveDownloader("nexus")
        {
            AlwaysThrow = new Modsync.Platform.Nexus.NexusAuthenticationException(
                "Nexus API key is not set. Authenticate with Nexus to continue."),
            PermanentType = typeof(Modsync.Platform.Nexus.NexusAuthenticationException),
        };

        var step = MakeStep(nexusDownloader);

        var act = async () => await step.ExecuteAsync(
            MakeInput(MakeManifest(archive), _downloadsPath),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Settings → Nexus*")
            .WithMessage("*API key*");
    }
}
