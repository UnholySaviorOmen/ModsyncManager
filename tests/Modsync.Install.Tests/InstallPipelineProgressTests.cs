// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Progress;
using Modsync.Install;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

/// <summary>
/// Проверяет, что InstallPipeline корректно репортит IProgress&lt;StepProgress&gt;.
///
/// Строит минимальный манифест + готовую структуру target-инстанса
/// с уже разложенными архивами и проверяет:
///   - каждый StepIndex 1..11 представлен хотя бы одним репортом;
///   - первый репорт каждого StepIndex идёт в порядке возрастания;
///   - StepName непустой у всех репортов;
///   - TotalSteps == 11 у всех репортов;
///   - StepIndex 1 — ReadManifest, StepIndex 11 — RegenerateProfile;
///   - репорты с Detail формата "Downloading: N / M" — только для
///     SyncArchives (StepIndex 6);
///   - репорты с Detail формата "Syncing: N / M mods" — только для
///     SyncMods (StepIndex 9).
///
/// Репортов может быть больше 11: SyncArchives репортит прогресс
/// после каждого завершённого архива, SyncMods — после каждого мода.
/// </summary>
public class InstallPipelineProgressTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly FileHashCache _hashCache = new();

    public InstallPipelineProgressTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-progress-" + Guid.NewGuid());
        _sourceDir = Path.Combine(_tempDir, "source");
        _targetDir = Path.Combine(_tempDir, "target");

        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private (string path, ModlistManifest manifest)
        PrepareManifestAndDownloads()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");
        Directory.CreateDirectory(downloadsDir);

        // --- MO2-архив ---
        var mo2ArchivePath = Path.Combine(
            downloadsDir, "Mod.Organizer-2.5.2.7z");
        var mo2ExeContent = Encoding.UTF8.GetBytes("fake MO2 exe content");
        using (var fs = File.Create(mo2ArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("ModOrganizer.exe");
            using var es = entry.Open();
            es.Write(mo2ExeContent, 0, mo2ExeContent.Length);
        }
        var mo2Hash = _hashCache.GetOrCompute(mo2ArchivePath);
        var mo2Size = new FileInfo(mo2ArchivePath).Length;

        // --- Mod-архив ---
        var modArchivePath = Path.Combine(downloadsDir, "TestMod.7z");
        var modFileContent = Encoding.UTF8.GetBytes("hello from mod");
        using (var fs = File.Create(modArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("file.txt");
            using var es = entry.Open();
            es.Write(modFileContent, 0, modFileContent.Length);
        }
        var modArchiveHash = _hashCache.GetOrCompute(modArchivePath);
        var modArchiveSize = new FileInfo(modArchivePath).Length;
        var modFileHash = XxHash64Value.FromStream(new MemoryStream(modFileContent));

        // --- Манифест ---
        var manifest = new ModlistManifest
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Progress Test Pack",
                Version = "1.0.0",
                Author = "tester",
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
                    Name = "Mod.Organizer-2.5.2.7z",
                    Size = mo2Size,
                    Hash = mo2Hash,
                    Sources = new Core.Models.Manifest.Sources.ArchiveSourceRef[]
                    {
                        new Core.Models.Manifest.Sources.MirrorSourceRef
                        {
                            Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                            Hash = mo2Hash,
                        },
                    },
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = new[]
            {
                new ArchiveEntry
                {
                    Id = "local_testmod",
                    Name = "TestMod.7z",
                    Size = modArchiveSize,
                    Hash = modArchiveHash,
                    Sources = new Core.Models.Manifest.Sources.ArchiveSourceRef[]
                    {
                        new Core.Models.Manifest.Sources.MirrorSourceRef
                        {
                            Url = "https://example.com/TestMod.7z",
                            Hash = modArchiveHash,
                        },
                    },
                },
            },
            Mods = new[]
            {
                new ModEntry
                {
                    Name = "TestMod",
                    Enabled = true,
                    Order = 0,
                    Meta = null,
                    Directives = new Core.Models.Manifest.Directives.Directive[]
                    {
                        new Core.Models.Manifest.Directives.FromArchiveDirective
                        {
                            Archive = "local_testmod",
                            Source = "file.txt",
                            Destination = "file.txt",
                            Hash = modFileHash,
                            Size = modFileContent.Length,
                        },
                    },
                },
            },
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

        var manifestPath = Path.Combine(_sourceDir, "modlist.json");
        File.WriteAllText(manifestPath, ManifestJson.Serialize(manifest));

        return (manifestPath, manifest);
    }

    private static InstallPipeline BuildPipeline()
    {
        var hashCache = new FileHashCache();
        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);
        var registry = new DownloaderRegistry(
            Array.Empty<IArchiveDownloader>());

        return new InstallPipeline(
            new ReadManifestStep(NullLogger<ReadManifestStep>.Instance),
            new ResolveTargetStep(NullLogger<ResolveTargetStep>.Instance),
            new ValidateTargetStep(NullLogger<ValidateTargetStep>.Instance),
            new BootstrapInstanceStep(NullLogger<BootstrapInstanceStep>.Instance),
            new BootstrapMo2Step(
                registry, hashCache, extractor,
                NullLogger<BootstrapMo2Step>.Instance),
            new SyncArchivesStep(
                registry, hashCache,
                NullLogger<SyncArchivesStep>.Instance),
            new GenerateArchiveMetaStep(
                NullLogger<GenerateArchiveMetaStep>.Instance),
            new ExecuteExtensionsStep(
                extractor,
                NullLogger<ExecuteExtensionsStep>.Instance),
            new ExecuteExtrasStep(
                extractor,
                NullLogger<ExecuteExtrasStep>.Instance),
            new SyncModsStep(
                extractor, hashCache,
                NullLogger<SyncModsStep>.Instance),
            new GenerateMetaIniStep(
                NullLogger<GenerateMetaIniStep>.Instance),
            new RegenerateProfileStep(
                NullLogger<RegenerateProfileStep>.Instance),
            NullLogger<InstallPipeline>.Instance);
    }

    private sealed class ListProgress : IProgress<StepProgress>
    {
        public List<StepProgress> Reports { get; } = new();
        public void Report(StepProgress value) => Reports.Add(value);
    }

    // ------------------------------------------------------------------
    //  Общая структура репортов
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithProgress_ReportsAllSteps()
    {
        var (manifestPath, _) = PrepareManifestAndDownloads();
        var pipeline = BuildPipeline();
        var progress = new ListProgress();

        await pipeline.ExecuteAsync(
            new InstallPipeline.Input
            {
                ManifestPath = manifestPath,
                Target = _targetDir,
                ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
            },
            CancellationToken.None,
            progress);

        // Репортов может быть больше 11: SyncArchives и SyncMods репортят
        // прогресс после каждого элемента. Проверяем структуру, а не
        // точное количество.

        progress.Reports.Should().NotBeEmpty();

        // Все TotalSteps == 12.
        progress.Reports.Should().OnlyContain(r => r.TotalSteps == 12);

        // StepName непустой у всех.
        progress.Reports.Select(r => r.StepName)
            .Should().NotContainNulls()
            .And.OnlyContain(s => !string.IsNullOrWhiteSpace(s));

        // Каждый StepIndex 1..12 представлен хотя бы одним репортом.
        var distinctIndices = progress.Reports
            .Select(r => r.StepIndex)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        distinctIndices.Should().Equal(Enumerable.Range(1, 12));

        // Первый репорт каждого StepIndex — в порядке возрастания.
        var firstReportByStep = progress.Reports
            .GroupBy(r => r.StepIndex)
            .OrderBy(g => g.Key)
            .Select(g => g.First())
            .ToList();

        firstReportByStep.Select(r => r.StepIndex)
            .Should().BeInAscendingOrder();

        firstReportByStep[0].StepName.Should().Be("ReadManifest");
        firstReportByStep[^1].StepName.Should().Be("RegenerateProfile");
    }

    [Fact]
    public async Task Execute_WithoutProgress_StillWorks()
    {
        var (manifestPath, _) = PrepareManifestAndDownloads();
        var pipeline = BuildPipeline();

        var output = await pipeline.ExecuteAsync(
            new InstallPipeline.Input
            {
                ManifestPath = manifestPath,
                Target = _targetDir,
                ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
            },
            CancellationToken.None);

        output.Manifest.Meta.Name.Should().Be("Progress Test Pack");
        Directory.Exists(output.InstancePath).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  SyncArchives: Detail = "Downloading: N / M"
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithProgress_SyncArchivesReportsDetail()
    {
        var (manifestPath, _) = PrepareManifestAndDownloads();
        var pipeline = BuildPipeline();
        var progress = new ListProgress();

        await pipeline.ExecuteAsync(
            new InstallPipeline.Input
            {
                ManifestPath = manifestPath,
                Target = _targetDir,
                ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
            },
            CancellationToken.None,
            progress);

        // Отбираем только репорты формата "Downloading: N / M"
        // (SyncArchives, StepIndex 6). Не путаем с SyncMods
        // ("Syncing: N / M mods", StepIndex 9).
        var downloadReports = progress.Reports
            .Where(r => r.Detail is not null
                     && r.Detail.StartsWith("Downloading: ", StringComparison.Ordinal))
            .ToList();

        downloadReports.Should().NotBeEmpty(
            "SyncArchives должен репортить прогресс скачивания");

        downloadReports.Should().AllSatisfy(r =>
        {
            r.StepIndex.Should().Be(6);
            r.StepName.Should().Be("SyncArchives");
        });

        // Формат: "Downloading: N / M".
        downloadReports.Should().AllSatisfy(r =>
            r.Detail.Should().MatchRegex(@"^Downloading: \d+ / \d+$"));
    }

    // ------------------------------------------------------------------
    //  SyncMods: Detail = "Syncing: N / M mods"
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WithProgress_SyncModsReportsDetail()
    {
        var (manifestPath, _) = PrepareManifestAndDownloads();
        var pipeline = BuildPipeline();
        var progress = new ListProgress();

        await pipeline.ExecuteAsync(
            new InstallPipeline.Input
            {
                ManifestPath = manifestPath,
                Target = _targetDir,
                ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
            },
            CancellationToken.None,
            progress);

        // Отбираем только репорты формата "Syncing: N / M mods"
        // (SyncMods, StepIndex 10).
        var syncReports = progress.Reports
            .Where(r => r.Detail is not null
                     && r.Detail.StartsWith("Syncing: ", StringComparison.Ordinal))
            .ToList();

        syncReports.Should().NotBeEmpty(
            "SyncMods должен репортить прогресс Pass 1");

        syncReports.Should().AllSatisfy(r =>
        {
            r.StepIndex.Should().Be(10);
            r.StepName.Should().Be("SyncMods");
        });

        // Формат: "Syncing: N / M mods".
        syncReports.Should().AllSatisfy(r =>
            r.Detail.Should().MatchRegex(@"^Syncing: \d+ / \d+ mods$"));
    }
}
