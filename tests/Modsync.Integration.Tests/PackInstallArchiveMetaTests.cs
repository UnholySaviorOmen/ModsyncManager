// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Modsync.Install.Verify;
using Modsync.Pack;
using Modsync.Pack.Steps;
using Modsync.Platform.MO2.Readers;
using Modsync.Platform.MO2.Writers;

namespace Modsync.Integration.Tests;

/// <summary>
/// Интеграционный тест pack → install → verify для .meta архивов.
///
/// Сценарий:
///   1. У автора в downloads/ лежит nexus-архив с .meta-файлом.
///   2. Packer читает .meta и сохраняет его в манифесте
///      (ArchiveEntry.Meta).
///   3. Installer восстанавливает .meta на целевой машине
///      (GenerateArchiveMetaStep).
///   4. Verify проверяет соответствие .meta манифесту.
///
/// Это ключевой сценарий блока 38: без восстановления .meta
/// при повторном pack nexus-архивы становились unmatched.
/// </summary>
public class PackInstallArchiveMetaTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceRoot;
    private readonly string _sourceInstance;
    private readonly string _targetDir;

    public PackInstallArchiveMetaTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-integ-meta-" + Guid.NewGuid());
        _sourceRoot = Path.Combine(_tempDir, "source");
        _sourceInstance = Path.Combine(_sourceRoot, "SourceInstance");
        _targetDir = Path.Combine(_tempDir, "target");

        Directory.CreateDirectory(_sourceRoot);
        Directory.CreateDirectory(_sourceInstance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Константы
    // ------------------------------------------------------------------

    private const string Mo2ArchiveName = "Mod.Organizer-2.5.2.7z";
    private const string ModArchiveName = "TestMod.7z";
    private const string ModName = "TestMod";
    private const string ProfileName = "Default";

    private static readonly ModMeta TestModMeta = new()
    {
        GameName = "Skyrim Special Edition",
        GameId = "skyrimspecialedition",
        ModId = 12345,
        FileId = 67890,
        Version = "1.0.0",
        Repository = "Nexus",
        Url = "https://www.nexusmods.com/skyrimspecialedition/mods/12345",
        Notes = "integration test note",
    };

    // ------------------------------------------------------------------
    //  Синтетический инстанс
    // ------------------------------------------------------------------

    private XxHash64Value BuildSyntheticInstance()
    {
        var mo2Dir = Path.Combine(_sourceInstance, "MO2");
        var downloadsDir = Path.Combine(mo2Dir, "downloads");
        var modsDir = Path.Combine(mo2Dir, "mods");
        var profileDir = Path.Combine(mo2Dir, "profiles", ProfileName);
        var stockGameDir = Path.Combine(_sourceInstance, "Stock Game");

        Directory.CreateDirectory(downloadsDir);
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(stockGameDir);

        // MO2-архив.
        var mo2ExeContent = Encoding.UTF8.GetBytes("fake MO2 exe");
        var mo2ArchivePath = Path.Combine(downloadsDir, Mo2ArchiveName);
        using (var fs = File.Create(mo2ArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("ModOrganizer.exe");
            using var es = entry.Open();
            es.Write(mo2ExeContent, 0, mo2ExeContent.Length);
        }
        var mo2Hash = XxHash64Value.FromFile(mo2ArchivePath);

        File.WriteAllBytes(Path.Combine(mo2Dir, "ModOrganizer.exe"), mo2ExeContent);

        // Mod-архив.
        var modFileContent = Encoding.UTF8.GetBytes("hello from mod");
        var modArchivePath = Path.Combine(downloadsDir, ModArchiveName);
        using (var fs = File.Create(modArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("file.txt");
            using var es = entry.Open();
            es.Write(modFileContent, 0, modFileContent.Length);
        }

        // .meta рядом с mod-архивом.
        File.WriteAllText(
            modArchivePath + ".meta",
            MetaIniWriter.Serialize(TestModMeta));

        // Папка мода.
        var testModDir = Path.Combine(modsDir, ModName);
        Directory.CreateDirectory(testModDir);
        File.WriteAllBytes(Path.Combine(testModDir, "file.txt"), modFileContent);

        // Профиль.
        File.WriteAllText(
            Path.Combine(profileDir, "modlist.txt"),
            "\uFEFF# This file was automatically generated by Mod Organizer.\r\n" +
            $"+{ModName}\r\n");
        File.WriteAllText(
            Path.Combine(profileDir, "plugins.txt"),
            "\uFEFF# Plugins\r\n*Test.esp\r\n");
        File.WriteAllText(
            Path.Combine(profileDir, "loadorder.txt"),
            "\uFEFF# Loadorder\r\nSkyrim.esm\r\nTest.esp\r\n");

        return mo2Hash;
    }

    private string WritePackConfig(XxHash64Value mo2Hash)
    {
        var configPath = Path.Combine(_sourceRoot, "modsyncmanager-pack.json");
        var json =
            $$"""
            {
              "meta": {
                "name": "Meta Roundtrip Pack",
                "version": "1.0.0",
                "author": "tester",
                "game": "skyrimspecialedition",
                "gameVersion": "1.6.1170"
              },
              "instance": { "path": "SourceInstance" },
              "mo2": {
                "version": "2.5.2",
                "profile": "{{ProfileName}}",
                "archive": "{{Mo2ArchiveName}}",
                "source": {
                  "type": "mirror",
                  "url": "https://example.com/Mod.Organizer-2.5.2.7z",
                  "hash": "{{mo2Hash}}"
                },
                "extensions": []
              },
              "stockGame": { "extras": [] },
              "archiveSources": []
            }
            """;
        File.WriteAllText(configPath, json);
        return configPath;
    }

    // ------------------------------------------------------------------
    //  Pipeline builders
    // ------------------------------------------------------------------

    private static PackPipeline BuildPackPipeline()
    {
        var hashCache = new FileHashCache();
        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        return new PackPipeline(
            new ReadConfigStep(NullLogger<ReadConfigStep>.Instance),
            new ReadInstanceStep(NullLogger<ReadInstanceStep>.Instance),
            new IndexArchivesStep(
                hashCache, NullLogger<IndexArchivesStep>.Instance),
            new ScanModsStep(
                hashCache, NullLogger<ScanModsStep>.Instance),
            new ScanExtensionsStep(
                hashCache, NullLogger<ScanExtensionsStep>.Instance),
            new ScanExtrasStep(
                hashCache, NullLogger<ScanExtrasStep>.Instance),
            new MatchStep(
                NullLogger<MatchStep>.Instance),
            new MatchExtensionsStep(
                NullLogger<MatchExtensionsStep>.Instance),
            new MatchExtrasStep(
                NullLogger<MatchExtrasStep>.Instance),
            new BuildManifestStep(
                NullLogger<BuildManifestStep>.Instance),
            new ValidateManifestStep(
                NullLogger<ValidateManifestStep>.Instance),
            new WriteManifestStep(
                NullLogger<WriteManifestStep>.Instance),
            extractor,
            hashCache,
            NullLoggerFactory.Instance,
            NullLogger<PackPipeline>.Instance);
    }

    private static InstallPipeline BuildInstallPipeline()
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

    private static VerifyPipeline BuildVerifyPipeline()
    {
        var hashCache = new FileHashCache();
        return new VerifyPipeline(
            hashCache, NullLogger<VerifyPipeline>.Instance);
    }

    private void PreloadTargetDownloads()
    {
        var srcDownloads = Path.Combine(_sourceInstance, "MO2", "downloads");
        var dstDownloads = Path.Combine(_targetDir, "MO2", "downloads");

        Directory.CreateDirectory(dstDownloads);

        foreach (var src in Directory.EnumerateFiles(
            srcDownloads, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(src);
            File.Copy(src, Path.Combine(dstDownloads, name), overwrite: true);
        }
    }

    private static ParallelOptions ParallelOptions() =>
        new() { MaxDegreeOfParallelism = 1 };

    // ------------------------------------------------------------------
    //  Тест 1 — Roundtrip с .meta
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_Then_Install_Then_Verify_WithArchiveMeta()
    {
        // === 1. Инстанс и конфиг ===
        var mo2Hash = BuildSyntheticInstance();
        var configPath = WritePackConfig(mo2Hash);

        // === 2. Packer ===
        var packPipeline = BuildPackPipeline();
        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, ParallelOptions()),
            CancellationToken.None);

        // Манифест содержит Meta для mod-архива.
        var modArchive = packResult.Manifest.Archives
            .Single(a => a.Name == ModArchiveName);
        modArchive.Meta.Should().NotBeNull();
        modArchive.Meta!.ModId.Should().Be(12345);
        modArchive.Meta!.FileId.Should().Be(67890);
        modArchive.Meta!.GameId.Should().Be("skyrimspecialedition");
        modArchive.Meta!.Version.Should().Be("1.0.0");
        modArchive.Meta!.Notes.Should().Be("integration test note");

        // MO2-архив — без .meta (у нас его нет) → Meta = null.
        packResult.Manifest.Mo2.Archive.Meta.Should().BeNull();

        // В JSON `meta` присутствует только у mod-архива.
        var json = File.ReadAllText(packResult.ManifestPath);
        json.Should().Contain("\"meta\"");
        json.Should().Contain("\"modId\": 12345");

        // === 3. Preload target downloads ===
        PreloadTargetDownloads();

        // Удалим .meta из target downloads/ — установщик должен его
        // восстановить из манифеста.
        var targetModArchiveMeta = Path.Combine(
            _targetDir, "MO2", "downloads", ModArchiveName + ".meta");
        File.Exists(targetModArchiveMeta).Should().BeTrue(
            "preload скопировал .meta");
        File.Delete(targetModArchiveMeta);
        File.Exists(targetModArchiveMeta).Should().BeFalse(
            "перед install .meta должен отсутствовать");

        // === 4. Installer ===
        var installPipeline = BuildInstallPipeline();
        var installOutput = await installPipeline.ExecuteAsync(
            InstallInputFactory.Create(
                packResult.ManifestPath,
                target: _targetDir,
                parallelOptions: ParallelOptions()),
            CancellationToken.None);

        // GenerateArchiveMetaStep восстановил .meta.
        installOutput.GenerateArchiveMeta.Written
            .Should().ContainSingle().Which.Should().Be(ModArchiveName);

        File.Exists(targetModArchiveMeta).Should().BeTrue(
            "installer восстановил .meta");

        var readMeta = MetaIniReader.TryRead(targetModArchiveMeta);
        readMeta.Should().NotBeNull();
        readMeta!.ModId.Should().Be(12345);
        readMeta.FileId.Should().Be(67890);
        readMeta.Version.Should().Be("1.0.0");
        readMeta.Notes.Should().Be("integration test note");

        // === 5. Verify ===
        var verifyPipeline = BuildVerifyPipeline();
        var report = verifyPipeline.Execute(_targetDir, CancellationToken.None);

        report.IsOk.Should().BeTrue(
            "failures: " + string.Join("; ",
                report.Failures.Select(f => f.Name + ": " + f.Message)));

        // Проверяем, что проверка .meta именно прошла.
        report.Checks.Should().Contain(c =>
            c.Name.Contains($"{ModArchiveName} / meta") && c.Passed);
    }

    // ------------------------------------------------------------------
    //  Тест 2 — Roundtrip с повреждённым .meta в verify
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_Then_Install_Then_Verify_WithDamagedArchiveMeta()
    {
        // === 1. Инстанс и конфиг ===
        var mo2Hash = BuildSyntheticInstance();
        var configPath = WritePackConfig(mo2Hash);

        // === 2. Packer ===
        var packPipeline = BuildPackPipeline();
        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, ParallelOptions()),
            CancellationToken.None);

        // === 3. Preload target downloads ===
        PreloadTargetDownloads();

        // === 4. Installer ===
        var installPipeline = BuildInstallPipeline();
        await installPipeline.ExecuteAsync(
            InstallInputFactory.Create(
                packResult.ManifestPath,
                target: _targetDir,
                parallelOptions: ParallelOptions()),
            CancellationToken.None);

        // === 5. Портим .meta в target ===
        var targetMetaPath = Path.Combine(
            _targetDir, "MO2", "downloads", ModArchiveName + ".meta");
        File.Exists(targetMetaPath).Should().BeTrue();

        // Записываем .meta с другим modID.
        var wrongMeta = TestModMeta with { ModId = 99999 };
        MetaIniWriter.WriteFile(targetMetaPath, wrongMeta);

        // === 6. Verify — должен обнаружить расхождение ===
        var verifyPipeline = BuildVerifyPipeline();
        var report = verifyPipeline.Execute(_targetDir, CancellationToken.None);

        report.IsOk.Should().BeFalse();

        var metaFailure = report.Failures
            .FirstOrDefault(f => f.Name.Contains($"{ModArchiveName} / meta"));
        metaFailure.Should().NotBeNull();
        metaFailure!.Message.Should().Contain("modID");
    }
}
