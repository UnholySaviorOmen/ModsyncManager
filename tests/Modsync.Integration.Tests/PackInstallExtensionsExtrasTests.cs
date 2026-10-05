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
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Modsync.Pack;
using Modsync.Pack.Steps;
using Modsync.Platform.Nexus;

namespace Modsync.Integration.Tests;

/// <summary>
/// Интеграционный тест pack → install с непустыми mo2.extensions
/// и stockGame.extras.
///
/// Покрывает два сценария для extensions:
///   1. Extension лежит в своём архиве (FomodTools.7z через archiveSources[])
///      → ExecuteExtensionsStep копирует файл в target → Written.
///   2. Extension лежит в MO2-архиве (plugins/fomod_plus.dll)
///      → BootstrapMo2Step уже распаковал → ExecuteExtensionsStep Skipped.
///
/// Extras (enbseries/enb.ini) лежат в mod-архиве (TestMod.7z), но
/// раскладываются в Stock Game/ — отдельная ветка.
///
/// Тесты:
///   1. Roundtrip: extensions и extras матчатся, восстанавливаются.
///   2. Unmatched: extensions и extras не матчатся, выгружаются в
///      __ModsyncManager_Output плоско (без промежуточной папки entry).
///   3. Idempotent: два прогона install подряд.
/// </summary>
public class PackInstallExtensionsExtrasTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceRoot;
    private readonly string _sourceInstance;
    private readonly string _targetDir;

    public PackInstallExtensionsExtrasTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-integ-ee-" + Guid.NewGuid());
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
    private const string FomodToolsArchiveName = "FomodTools.7z";
    private const string ModName = "TestMod";
    private const string ProfileName = "Default";

    private const string Mo2ArchiveId = "local_mod-organizer-2-5-2";
    private const string ModArchiveId = "nexus_skyrimspecialedition_12345_67890";
    private const string FomodToolsArchiveId = "local_fomodtools";

    private const string FomodDllEntryName = "plugins/fomod.dll";
    private const string FomodPlusDllEntryName = "plugins/fomod_plus.dll";
    private const string ExtraEntryName = "enbseries";
    private const string ExtraFilePath = "enbseries/enb.ini";

    // ------------------------------------------------------------------
    //  Синтетический инстанс
    // ------------------------------------------------------------------

    private sealed record SyntheticInstance(
        XxHash64Value Mo2Hash,
        XxHash64Value ModArchiveHash,
        XxHash64Value FomodToolsHash);

    /// <summary>
    /// Строит синтетический инстанс с расширениями и extras.
    ///
    /// MO2-архив содержит:
    ///   - ModOrganizer.exe
    ///   - plugins/fomod_plus.dll     (extension, matched с MO2-архивом)
    ///
    /// FomodTools.7z содержит:
    ///   - plugins/fomod.dll          (extension, matched со своим архивом)
    ///
    /// TestMod.7z содержит:
    ///   - file.txt
    ///   - enbseries/enb.ini          (extra, matched с mod-архивом)
    ///
    /// В инстансе на диске всё разложено так же (чтобы pack нашёл файлы).
    /// </summary>
    private SyntheticInstance BuildSyntheticInstanceWithExtensionsAndExtras()
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

        var mo2ExeContent = Encoding.UTF8.GetBytes("fake MO2 exe content");
        var fomodPlusContent = Encoding.UTF8.GetBytes("fomod plus dll content");
        var fomodContent = Encoding.UTF8.GetBytes("fomod dll content");

        // --- MO2-архив ---
        var mo2ArchivePath = Path.Combine(downloadsDir, Mo2ArchiveName);
        using (var fs = File.Create(mo2ArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("ModOrganizer.exe");
            using (var s = e1.Open())
                s.Write(mo2ExeContent, 0, mo2ExeContent.Length);

            var e2 = zip.CreateEntry(FomodPlusDllEntryName);
            using (var s = e2.Open())
                s.Write(fomodPlusContent, 0, fomodPlusContent.Length);
        }
        var mo2Hash = XxHash64Value.FromFile(mo2ArchivePath);

        // Распакованный MO2/ в инстансе.
        File.WriteAllBytes(
            Path.Combine(mo2Dir, "ModOrganizer.exe"), mo2ExeContent);

        var fomodPlusDiskPath = Path.Combine(
            mo2Dir,
            FomodPlusDllEntryName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fomodPlusDiskPath)!);
        File.WriteAllBytes(fomodPlusDiskPath, fomodPlusContent);

        // --- FomodTools.7z ---
        var fomodArchivePath = Path.Combine(downloadsDir, FomodToolsArchiveName);
        using (var fs = File.Create(fomodArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry(FomodDllEntryName);
            using (var s = e1.Open())
                s.Write(fomodContent, 0, fomodContent.Length);
        }
        var fomodToolsHash = XxHash64Value.FromFile(fomodArchivePath);

        var fomodDiskPath = Path.Combine(
            mo2Dir,
            FomodDllEntryName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fomodDiskPath)!);
        File.WriteAllBytes(fomodDiskPath, fomodContent);

        // --- mod-архив TestMod.7z ---
        var modFileContent = Encoding.UTF8.GetBytes("hello from mod");
        var enbContent = Encoding.UTF8.GetBytes("enb ini content");

        var modArchivePath = Path.Combine(downloadsDir, ModArchiveName);
        using (var fs = File.Create(modArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("file.txt");
            using (var s = e1.Open())
                s.Write(modFileContent, 0, modFileContent.Length);

            var e2 = zip.CreateEntry(ExtraFilePath);
            using (var s = e2.Open())
                s.Write(enbContent, 0, enbContent.Length);
        }
        var modArchiveHash = XxHash64Value.FromFile(modArchivePath);

        File.WriteAllText(
            modArchivePath + ".meta",
            "[General]\r\n" +
            "gameName=Skyrim Special Edition\r\n" +
            "modID=12345\r\n" +
            "fileID=67890\r\n");

        // --- папка мода TestMod ---
        var testModDir = Path.Combine(modsDir, ModName);
        Directory.CreateDirectory(testModDir);
        File.WriteAllBytes(Path.Combine(testModDir, "file.txt"), modFileContent);

        // --- extras в Stock Game/ ---
        var enbDiskPath = Path.Combine(
            stockGameDir,
            ExtraFilePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(enbDiskPath)!);
        File.WriteAllBytes(enbDiskPath, enbContent);

        // --- профиль ---
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

        return new SyntheticInstance(mo2Hash, modArchiveHash, fomodToolsHash);
    }

    /// <summary>
    /// Дописывает в инстанс файлы, которых нет ни в одном архиве —
    /// для unmatched-сценария.
    /// </summary>
    private void AddUnmatchedExtensionAndExtra()
    {
        var mo2Dir = Path.Combine(_sourceInstance, "MO2");
        var extraDllPath = Path.Combine(mo2Dir, "plugins", "extra.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(extraDllPath)!);
        File.WriteAllText(extraDllPath, "unmatched extension");

        var stockGameDir = Path.Combine(_sourceInstance, "Stock Game");
        File.WriteAllText(
            Path.Combine(stockGameDir, "custom-file.txt"),
            "unmatched extra");
    }

    // ------------------------------------------------------------------
    //  modsyncmanager-pack.json
    // ------------------------------------------------------------------

    private string WritePackConfig(
        SyntheticInstance instance,
        string[] extensions,
        string[] extras,
        bool includeFomodToolsArchive = true)
    {
        var configPath = Path.Combine(_sourceRoot, "modsyncmanager-pack.json");

        var extensionsJson = "[" +
            string.Join(", ", extensions.Select(e => $"\"{e}\"")) + "]";
        var extrasJson = "[" +
            string.Join(", ", extras.Select(e => $"\"{e}\"")) + "]";

        var archiveSourcesJson = includeFomodToolsArchive
            ? $$"""
              [
                {
                  "archive": "{{FomodToolsArchiveName}}",
                  "sources": [
                    {
                      "type": "mirror",
                      "url": "https://example.com/FomodTools.7z",
                      "hash": "xxh64:0000000000000001"
                    }
                  ]
                }
              ]
              """
            : "[]";

        var json =
            $$"""
            {
              "meta": {
                "name": "EE Roundtrip Pack",
                "version": "1.0.0",
                "author": "tester",
                "game": "skyrimspecialedition",
                "gameVersion": "1.6.1170"
              },
              "instance": {
                "path": "SourceInstance"
              },
              "mo2": {
                "version": "2.5.2",
                "profile": "{{ProfileName}}",
                "archive": "{{Mo2ArchiveName}}",
                "source": {
                  "type": "mirror",
                  "url": "https://example.com/Mod.Organizer-2.5.2.7z",
                  "hash": "{{instance.Mo2Hash}}"
                },
                "extensions": {{extensionsJson}}
              },
              "stockGame": {
                "extras": {{extrasJson}}
              },
              "archiveSources": {{archiveSourcesJson}}
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
        var loggerFactory = NullLoggerFactory.Instance;

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
            loggerFactory,
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
            new PreflightNexusAuthStep(
                new NexusApiKeyProviderStub(),
                NullLogger<PreflightNexusAuthStep>.Instance),
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
    //  Тест 1 — Roundtrip с extensions и extras
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_Then_Install_WithExtensionsAndExtras_Roundtrip()
    {
        // === 1. Инстанс и конфиг ===
        var instance = BuildSyntheticInstanceWithExtensionsAndExtras();
        var configPath = WritePackConfig(
            instance,
            extensions: new[] { FomodDllEntryName, FomodPlusDllEntryName },
            extras: new[] { ExtraEntryName });

        // === 2. Packer ===
        var packPipeline = BuildPackPipeline();
        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, ParallelOptions()),
            CancellationToken.None);

        var manifest = packResult.Manifest;

        // --- Manifest: extensions — два entry ---
        manifest.Mo2.Extensions.Should().HaveCount(2);

        var fomodDll = manifest.Mo2.Extensions
            .Single(e => e.Name == FomodDllEntryName);
        fomodDll.Directives.Should().HaveCount(1);
        var d1 = fomodDll.Directives[0]
            .Should().BeOfType<FromArchiveDirective>().Subject;
        d1.Archive.Should().Be(FomodToolsArchiveId);
        d1.Source.Should().Be(FomodDllEntryName);
        d1.Destination.Should().Be(FomodDllEntryName);

        var fomodPlus = manifest.Mo2.Extensions
            .Single(e => e.Name == FomodPlusDllEntryName);
        fomodPlus.Directives.Should().HaveCount(1);
        var d2 = fomodPlus.Directives[0]
            .Should().BeOfType<FromArchiveDirective>().Subject;
        d2.Archive.Should().Be(Mo2ArchiveId);
        d2.Source.Should().Be(FomodPlusDllEntryName);
        d2.Destination.Should().Be(FomodPlusDllEntryName);

        // --- Manifest: extras ---
        manifest.StockGame.Extras.Should().HaveCount(1);
        var xst = manifest.StockGame.Extras[0];
        xst.Name.Should().Be(ExtraEntryName);
        xst.Directives.Should().HaveCount(1);
        var xstDir = xst.Directives[0]
            .Should().BeOfType<FromArchiveDirective>().Subject;
        xstDir.Archive.Should().Be(ModArchiveId);
        xstDir.Source.Should().Be(ExtraFilePath);
        xstDir.Destination.Should().Be(ExtraFilePath);

        // --- Manifest.Archives содержит FomodTools.7z и TestMod.7z ---
        manifest.Archives.Should().Contain(a => a.Id == FomodToolsArchiveId);
        manifest.Archives.Should().Contain(a => a.Id == ModArchiveId);
        manifest.Archives.Should().NotContain(a => a.Id == Mo2ArchiveId);

        // --- __ModsyncManager_Output: unmatched extensions/extras пусто ---
        var outputMo2 = Path.Combine(
            _sourceInstance, "__ModsyncManager_Output", "MO2");
        var outputStockGame = Path.Combine(
            _sourceInstance, "__ModsyncManager_Output", "Stock Game");

        File.Exists(Path.Combine(outputMo2, "plugins", "fomod.dll"))
            .Should().BeFalse("extension matched → не должно быть в unmatched");
        File.Exists(Path.Combine(outputMo2, "plugins", "fomod_plus.dll"))
            .Should().BeFalse("extension matched → не должно быть в unmatched");
        File.Exists(Path.Combine(outputStockGame, "enbseries", "enb.ini"))
            .Should().BeFalse("extra matched → не должно быть в unmatched");

        // --- JSON на диске: полиморфизм директив сохраняется ---
        var json = File.ReadAllText(packResult.ManifestPath);
        json.Should().Contain("\"extensions\"");
        json.Should().Contain("\"extras\"");
        json.Should().Contain("\"type\": \"FromArchive\"");
        json.Should().Contain($"\"name\": \"{FomodDllEntryName}\"");
        json.Should().Contain($"\"name\": \"{FomodPlusDllEntryName}\"");
        json.Should().Contain($"\"name\": \"{ExtraEntryName}\"");

        // === 3. Preload target downloads ===
        PreloadTargetDownloads();

        // === 4. Installer ===
        var installPipeline = BuildInstallPipeline();
        var installOutput = await installPipeline.ExecuteAsync(
            InstallInputFactory.Create(
                packResult.ManifestPath,
                target: _targetDir,
                parallelOptions: ParallelOptions()),
            CancellationToken.None);

        // --- Файлы на диске ---
        var fomodDllInTarget = Path.Combine(
            _targetDir, "MO2", "plugins", "fomod.dll");
        File.Exists(fomodDllInTarget).Should().BeTrue();
        File.ReadAllText(fomodDllInTarget).Should().Be("fomod dll content");

        var fomodPlusInTarget = Path.Combine(
            _targetDir, "MO2", "plugins", "fomod_plus.dll");
        File.Exists(fomodPlusInTarget).Should().BeTrue();
        File.ReadAllText(fomodPlusInTarget).Should().Be("fomod plus dll content");

        var enbInTarget = Path.Combine(
            _targetDir, "Stock Game", "enbseries", "enb.ini");
        File.Exists(enbInTarget).Should().BeTrue();
        File.ReadAllText(enbInTarget).Should().Be("enb ini content");

        // --- Результаты Execute* шагов ---
        // fomod.dll — из своего архива → Written.
        // fomod_plus.dll — уже распакован BootstrapMo2Step → Skipped.
        installOutput.ExecuteExtensions.Written
            .Should().ContainSingle().Which.Should().Be(FomodDllEntryName);
        installOutput.ExecuteExtensions.Skipped
            .Should().ContainSingle().Which.Should().Be(FomodPlusDllEntryName);

        installOutput.ExecuteExtras.Written
            .Should().ContainSingle().Which.Should().Be(ExtraEntryName);
        installOutput.ExecuteExtras.Skipped.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Тест 2 — Unmatched extensions и extras
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_Then_Install_WithUnmatchedExtensionsAndExtras()
    {
        // === 1. Инстанс и конфиг ===
        var instance = BuildSyntheticInstanceWithExtensionsAndExtras();
        AddUnmatchedExtensionAndExtra();

        var configPath = WritePackConfig(
            instance,
            extensions: new[]
            {
                FomodDllEntryName,
                FomodPlusDllEntryName,
                "plugins/extra.dll",
            },
            extras: new[] { ExtraEntryName, "custom-file.txt" });

        // === 2. Packer ===
        var packPipeline = BuildPackPipeline();
        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, ParallelOptions()),
            CancellationToken.None);

        var manifest = packResult.Manifest;

        // --- Manifest: extensions содержит только matched entry ---
        manifest.Mo2.Extensions.Should().HaveCount(2);
        manifest.Mo2.Extensions.Should().Contain(e => e.Name == FomodDllEntryName);
        manifest.Mo2.Extensions.Should().Contain(e => e.Name == FomodPlusDllEntryName);
        manifest.Mo2.Extensions.Should().NotContain(e => e.Name == "plugins/extra.dll");

        // --- Manifest: extras содержит только matched entry ---
        manifest.StockGame.Extras.Should().HaveCount(1);
        manifest.StockGame.Extras[0].Name.Should().Be(ExtraEntryName);
        manifest.StockGame.Extras.Should().NotContain(e => e.Name == "custom-file.txt");

        // --- __ModsyncManager_Output: unmatched extensions плоско ---
        var outputMo2 = Path.Combine(
            _sourceInstance, "__ModsyncManager_Output", "MO2");
        var unmatchedExtPath = Path.Combine(outputMo2, "plugins", "extra.dll");
        File.Exists(unmatchedExtPath).Should().BeTrue();
        File.ReadAllText(unmatchedExtPath).Should().Be("unmatched extension");

        // --- __ModsyncManager_Output: unmatched extras плоско ---
        var outputStockGame = Path.Combine(
            _sourceInstance, "__ModsyncManager_Output", "Stock Game");
        var unmatchedExtraPath = Path.Combine(outputStockGame, "custom-file.txt");
        File.Exists(unmatchedExtraPath).Should().BeTrue();
        File.ReadAllText(unmatchedExtraPath).Should().Be("unmatched extra");

        // --- matched НЕ попадают в __ModsyncManager_Output ---
        File.Exists(Path.Combine(outputMo2, "plugins", "fomod.dll"))
            .Should().BeFalse();
        File.Exists(Path.Combine(outputMo2, "plugins", "fomod_plus.dll"))
            .Should().BeFalse();
        File.Exists(Path.Combine(outputStockGame, "enbseries", "enb.ini"))
            .Should().BeFalse();

        // === 3. Preload target downloads ===
        PreloadTargetDownloads();

        // === 4. Installer ===
        var installPipeline = BuildInstallPipeline();
        var installOutput = await installPipeline.ExecuteAsync(
            InstallInputFactory.Create(
                packResult.ManifestPath,
                target: _targetDir,
                parallelOptions: ParallelOptions()),
            CancellationToken.None);

        // --- Matched файлы на диске ---
        File.Exists(Path.Combine(_targetDir, "MO2", "plugins", "fomod.dll"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_targetDir, "MO2", "plugins", "fomod_plus.dll"))
            .Should().BeTrue();
        File.Exists(Path.Combine(
            _targetDir, "Stock Game", "enbseries", "enb.ini"))
            .Should().BeTrue();

        // --- Unmatched НЕ разложены (их нет в манифесте) ---
        File.Exists(Path.Combine(_targetDir, "MO2", "plugins", "extra.dll"))
            .Should().BeFalse();
        File.Exists(Path.Combine(_targetDir, "Stock Game", "custom-file.txt"))
            .Should().BeFalse();

        // --- Результаты Execute* шагов ---
        installOutput.ExecuteExtensions.Written
            .Should().ContainSingle().Which.Should().Be(FomodDllEntryName);
        installOutput.ExecuteExtensions.Skipped
            .Should().ContainSingle().Which.Should().Be(FomodPlusDllEntryName);
        installOutput.ExecuteExtras.Written
            .Should().ContainSingle().Which.Should().Be(ExtraEntryName);
    }

    // ------------------------------------------------------------------
    //  Тест 3 — Идемпотентность
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_Then_Install_ExtensionsAndExtras_AreIdempotent()
    {
        // === 1. Инстанс и конфиг ===
        var instance = BuildSyntheticInstanceWithExtensionsAndExtras();
        var configPath = WritePackConfig(
            instance,
            extensions: new[] { FomodDllEntryName, FomodPlusDllEntryName },
            extras: new[] { ExtraEntryName });

        // === 2. Packer ===
        var packPipeline = BuildPackPipeline();
        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, ParallelOptions()),
            CancellationToken.None);

        PreloadTargetDownloads();

        // === 3. Два прогона installer-а ===
        var installPipeline = BuildInstallPipeline();
        var input = InstallInputFactory.Create(
            packResult.ManifestPath,
            target: _targetDir,
            parallelOptions: ParallelOptions());

        var first = await installPipeline.ExecuteAsync(
            input, CancellationToken.None);

        var fomodPath = Path.Combine(
            _targetDir, "MO2", "plugins", "fomod.dll");
        var fomodPlusPath = Path.Combine(
            _targetDir, "MO2", "plugins", "fomod_plus.dll");
        var enbPath = Path.Combine(
            _targetDir, "Stock Game", "enbseries", "enb.ini");

        var fomodTimeAfterFirst = File.GetLastWriteTimeUtc(fomodPath);
        var fomodPlusTimeAfterFirst = File.GetLastWriteTimeUtc(fomodPlusPath);
        var enbTimeAfterFirst = File.GetLastWriteTimeUtc(enbPath);

        // Небольшая пауза — на всякий случай. Если файл перезаписывается,
        // mtime меняется; если нет — остаётся.
        await Task.Delay(50);

        var second = await installPipeline.ExecuteAsync(
            input, CancellationToken.None);

        // --- Первый прогон ---
        // fomod.dll — из FomodTools.7z, BootstrapMo2Step его не распаковывает.
        //   Значит первый ExecuteExtensionsStep его копирует → Written.
        // fomod_plus.dll — в MO2-архиве, BootstrapMo2Step распаковал.
        //   Значит Skipped.
        first.ExecuteExtensions.Written
            .Should().ContainSingle().Which.Should().Be(FomodDllEntryName);
        first.ExecuteExtensions.Skipped
            .Should().ContainSingle().Which.Should().Be(FomodPlusDllEntryName);

        first.ExecuteExtras.Written
            .Should().ContainSingle().Which.Should().Be(ExtraEntryName);
        first.ExecuteExtras.Skipped.Should().BeEmpty();

        // --- Второй прогон: всё Skipped ---
        second.ExecuteExtensions.Written.Should().BeEmpty();
        second.ExecuteExtensions.Skipped.Should().HaveCount(2);
        second.ExecuteExtensions.Skipped.Should().Contain(FomodDllEntryName);
        second.ExecuteExtensions.Skipped.Should().Contain(FomodPlusDllEntryName);

        second.ExecuteExtras.Written.Should().BeEmpty();
        second.ExecuteExtras.Skipped
            .Should().ContainSingle().Which.Should().Be(ExtraEntryName);

        // --- Файлы не перезаписывались вторым прогоном ---
        File.GetLastWriteTimeUtc(fomodPath).Should().Be(fomodTimeAfterFirst);
        File.GetLastWriteTimeUtc(fomodPlusPath).Should().Be(fomodPlusTimeAfterFirst);
        File.GetLastWriteTimeUtc(enbPath).Should().Be(enbTimeAfterFirst);
    }
}
