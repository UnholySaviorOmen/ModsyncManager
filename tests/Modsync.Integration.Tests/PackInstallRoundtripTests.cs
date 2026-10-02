// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Modsync.Core.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Archives.Extraction;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Modsync.Pack;
using Modsync.Pack.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Integration.Tests;

/// <summary>
/// Интеграционный тест pack → install.
///
/// Строит синтетический инстанс MO2, прогоняет PackPipeline,
/// затем прогоняет InstallPipeline в чистую папку.
/// Проверяет, что инстанс полностью воссоздан.
/// </summary>
public class PackInstallRoundtripTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceRoot;     // инстанс автора (вход packer-а)
    private readonly string _sourceInstance; // = _sourceRoot/SourceInstance
    private readonly string _targetDir;      // чистая папка для installer-а

    public PackInstallRoundtripTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-integration-" + Guid.NewGuid());
        _sourceRoot = Path.Combine(_tempDir, "source");
        _sourceInstance = Path.Combine(_sourceRoot, "SourceInstance");
        _targetDir = Path.Combine(_tempDir, "target");

        Directory.CreateDirectory(_sourceRoot);
        Directory.CreateDirectory(_sourceInstance);
        // target НЕ создаём — InstallPipeline должен сам.
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Построение синтетического инстанса
    // ------------------------------------------------------------------

    private const string Mo2ArchiveName = "Mod.Organizer-2.5.2.7z";

    private (XxHash64Value mo2Hash,
             XxHash64Value modArchiveHash,
             XxHash64Value modFileHash)
        BuildSyntheticInstance()
    {
        var mo2Dir = Path.Combine(_sourceInstance, "MO2");
        var downloadsDir = Path.Combine(mo2Dir, "downloads");
        var modsDir = Path.Combine(mo2Dir, "mods");
        var profileDir = Path.Combine(mo2Dir, "profiles", "Default");
        var stockGameDir = Path.Combine(_sourceInstance, "Stock Game");

        Directory.CreateDirectory(downloadsDir);
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(stockGameDir);

        // --- MO2-архив: zip с ModOrganizer.exe ---
        var mo2ArchivePath = Path.Combine(downloadsDir, Mo2ArchiveName);
        var mo2ExeContent = Encoding.UTF8.GetBytes("fake MO2 exe content");
        using (var fs = File.Create(mo2ArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("ModOrganizer.exe");
            using var es = entry.Open();
            es.Write(mo2ExeContent, 0, mo2ExeContent.Length);
        }
        var mo2Hash = XxHash64Value.FromFile(mo2ArchivePath);

        // В SourceInstance/MO2/ лежит уже распакованный ModOrganizer.exe,
        // чтобы packer мог прочитать инстанс.
        File.WriteAllBytes(Path.Combine(mo2Dir, "ModOrganizer.exe"), mo2ExeContent);

        // --- mod-архив: zip с file.txt ---
        var modArchivePath = Path.Combine(downloadsDir, "TestMod.7z");
        var modFileContent = Encoding.UTF8.GetBytes("hello from mod");
        using (var fs = File.Create(modArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("file.txt");
            using var es = entry.Open();
            es.Write(modFileContent, 0, modFileContent.Length);
        }
        var modArchiveHash = XxHash64Value.FromFile(modArchivePath);
        var modFileHash = XxHash64Value.FromStream(new MemoryStream(modFileContent));

        // .meta для mod-архива (Nexus).
        File.WriteAllText(
            modArchivePath + ".meta",
            "[General]\r\n" +
            "gameName=Skyrim Special Edition\r\n" +
            "modID=12345\r\n" +
            "fileID=67890\r\n");

        // --- папка мода TestMod ---
        var testModDir = Path.Combine(modsDir, "TestMod");
        Directory.CreateDirectory(testModDir);
        File.WriteAllBytes(Path.Combine(testModDir, "file.txt"), modFileContent);

        // meta.ini с lowercase-ключами — проверим case-insensitive fix.
        File.WriteAllText(
            Path.Combine(testModDir, "meta.ini"),
            "[General]\r\n" +
            "gameName=Skyrim Special Edition\r\n" +
            "gameID=skyrimspecialedition\r\n" +
            "modid=12345\r\n" +
            "fileid=67890\r\n" +
            "version=1.0.0\r\n" +
            "notes=integration test\r\n");

        // --- папка отключённого мода ---
        var disabledModDir = Path.Combine(modsDir, "DisabledMod");
        Directory.CreateDirectory(disabledModDir);
        File.WriteAllText(Path.Combine(disabledModDir, "readme.txt"), "disabled");

        // --- папка [NoDelete] ---
        var noDeleteDir = Path.Combine(modsDir, "[NoDelete]UserMod");
        Directory.CreateDirectory(noDeleteDir);
        File.WriteAllText(Path.Combine(noDeleteDir, "user.txt"), "user data");

        // --- профиль ---
        File.WriteAllText(
            Path.Combine(profileDir, "modlist.txt"),
            "\uFEFF# This file was automatically generated by Mod Organizer.\r\n" +
            "+TestMod\r\n" +
            "-DisabledMod\r\n" +
            "-# \U0001F4C2 Мои моды_separator\r\n");

        File.WriteAllText(
            Path.Combine(profileDir, "plugins.txt"),
            "\uFEFF# Plugins\r\n" +
            "*Test.esp\r\n");

        File.WriteAllText(
            Path.Combine(profileDir, "loadorder.txt"),
            "\uFEFF# Loadorder\r\n" +
            "Skyrim.esm\r\n" +
            "Test.esp\r\n");

        return (mo2Hash, modArchiveHash, modFileHash);
    }

    private string WritePackConfig(XxHash64Value mo2Hash)
    {
        var configPath = Path.Combine(_sourceRoot, "modsyncmanager-pack.json");
        var json =
            $$"""
            {
              "meta": {
                "name": "Roundtrip Pack",
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
                "profile": "Default",
                "archive": "{{Mo2ArchiveName}}",
                "source": {
                  "type": "mirror",
                  "url": "https://example.com/Mod.Organizer-2.5.2.7z",
                  "hash": "{{mo2Hash}}"
                },
                "extensions": []
              },
              "stockGame": {
                "extras": []
              },
              "archiveSources": []
            }
            """;
        File.WriteAllText(configPath, json);
        return configPath;
    }

    // ------------------------------------------------------------------
    //  Сборка пайплайнов руками
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
            new ResolveTargetStep(NullLogger<ResolveTargetStep>.Instance),
            new ValidateTargetStep(NullLogger<ValidateTargetStep>.Instance),
            new BootstrapInstanceStep(NullLogger<BootstrapInstanceStep>.Instance),
            new BootstrapMo2Step(
                registry, hashCache, extractor,
                NullLogger<BootstrapMo2Step>.Instance),
            new SyncArchivesStep(
                registry, hashCache,
                NullLogger<SyncArchivesStep>.Instance),
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

        foreach (var src in Directory.EnumerateFiles(srcDownloads, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(src);
            File.Copy(src, Path.Combine(dstDownloads, name), overwrite: true);
        }
    }

    // ------------------------------------------------------------------
    //  Главный тест
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_Then_Install_Roundtrip()
    {
        // === 1. Готовим инстанс и конфиг ===
        var (mo2Hash, _, _) = BuildSyntheticInstance();
        var configPath = WritePackConfig(mo2Hash);

        // === 2. Packer ===
        var packPipeline = BuildPackPipeline();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 };

        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, parallelOptions),
            CancellationToken.None);

        // --- Проверяем манифест ---
        var manifest = packResult.Manifest;
        manifest.Meta.Name.Should().Be("Roundtrip Pack");
        manifest.Mo2.Profile.Should().Be("Default");

        manifest.Mods.Should().HaveCount(3);

        var testMod = manifest.Mods.Single(m => m.Name == "TestMod");
        testMod.Enabled.Should().BeTrue();
        testMod.Meta.Should().NotBeNull();
        testMod.Meta!.ModId.Should().Be(12345);
        testMod.Meta!.FileId.Should().Be(67890);
        testMod.Meta!.Version.Should().Be("1.0.0");
        testMod.Directives.Should().HaveCount(1);

        var disabledMod = manifest.Mods.Single(m => m.Name == "DisabledMod");
        disabledMod.Enabled.Should().BeFalse();
        disabledMod.Directives.Should().BeEmpty();

        manifest.Mods.Should().Contain(m => m.Name.StartsWith("#"));

        manifest.Mods.Should().NotContain(m => m.Name.Contains("[NoDelete]"));

        packResult.Match.Unmatched.Should().Contain(u =>
            u.ModName == "DisabledMod" && u.RelativePath == "readme.txt");

        var sourceUnmatched = Path.Combine(
            _sourceInstance,
            "__ModsyncManager_Output", "MO2", "mods", "DisabledMod", "readme.txt");
        File.Exists(sourceUnmatched).Should().BeTrue();
        File.ReadAllText(sourceUnmatched).Should().Be("disabled");

        manifest.Mo2.Archive.Name.Should().Be(Mo2ArchiveName);
        manifest.Archives.Should().NotContain(a => a.Name == Mo2ArchiveName);

        manifest.Archives.Should().Contain(a => a.Name == "TestMod.7z");

        // === 3. Preload target downloads ===
        PreloadTargetDownloads();

        // === 4. Installer ===
        var manifestPath = packResult.ManifestPath;
        var installPipeline = BuildInstallPipeline();

        var installOutput = await installPipeline.ExecuteAsync(
            InstallInputFactory.Create(
                manifestPath,
                target: _targetDir,
                parallelOptions: parallelOptions),
            CancellationToken.None);

        // === 5. Проверяем target-инстанс ===

        installOutput.InstancePath.Should().Be(Path.GetFullPath(_targetDir));
        Directory.Exists(Path.Combine(_targetDir, "MO2")).Should().BeTrue();
        Directory.Exists(Path.Combine(_targetDir, "MO2", "mods")).Should().BeTrue();
        Directory.Exists(Path.Combine(_targetDir, "MO2", "profiles", "Default")).Should().BeTrue();
        Directory.Exists(Path.Combine(_targetDir, "Stock Game")).Should().BeTrue();

        File.Exists(Path.Combine(_targetDir, "modlist.json")).Should().BeTrue();

        File.Exists(Path.Combine(_targetDir, "MO2", "ModOrganizer.exe")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_targetDir, "MO2", "ModOrganizer.exe"))
            .Should().Be("fake MO2 exe content");

        var testModTarget = Path.Combine(_targetDir, "MO2", "mods", "TestMod");
        Directory.Exists(testModTarget).Should().BeTrue();
        File.Exists(Path.Combine(testModTarget, "file.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(testModTarget, "file.txt"))
            .Should().Be("hello from mod");

        var metaIniPath = Path.Combine(testModTarget, "meta.ini");
        File.Exists(metaIniPath).Should().BeTrue();
        var metaIniText = File.ReadAllText(metaIniPath);
        metaIniText.Should().Contain("modID=12345");
        metaIniText.Should().Contain("fileID=67890");
        metaIniText.Should().Contain("version=1.0.0");
        metaIniText.Should().Contain("notes=integration test");

        var disabledModTarget = Path.Combine(_targetDir, "MO2", "mods", "DisabledMod");
        Directory.Exists(disabledModTarget).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(disabledModTarget).Should().BeEmpty();

        var noDeleteTarget = Path.Combine(_targetDir, "MO2", "mods", "[NoDelete]UserMod");
        Directory.Exists(noDeleteTarget).Should().BeFalse();

        var modlistTxtPath = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "modlist.txt");
        File.Exists(modlistTxtPath).Should().BeTrue();
        var modlistLines = File.ReadAllLines(modlistTxtPath);

        modlistLines.Should().Contain("+TestMod");
        modlistLines.Should().Contain("-DisabledMod");
        modlistLines.Should().Contain(l => l.StartsWith("-#") && l.Contains("Мои моды_separator"));

        var orderedBody = modlistLines
            .Where(l => l.Length > 0 && !l.StartsWith("# This file"))
            .ToList();
        orderedBody[0].Should().Be("+TestMod");
        orderedBody[1].Should().Be("-DisabledMod");
        orderedBody[2].Should().StartWith("-#");

        var pluginsTxtPath = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "plugins.txt");
        File.Exists(pluginsTxtPath).Should().BeTrue();
        var pluginsText = File.ReadAllText(pluginsTxtPath);
        pluginsText.Should().Contain("*Test.esp");

        var loadorderTxtPath = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "loadorder.txt");
        File.Exists(loadorderTxtPath).Should().BeTrue();
        var loadorderLines = File.ReadAllLines(loadorderTxtPath)
            .Where(l => l.Length > 0 && !l.StartsWith("#"))
            .ToList();
        loadorderLines.Should().Equal("Skyrim.esm", "Test.esp");

        installOutput.SyncMods.Created.Should().Contain("TestMod");
        installOutput.SyncMods.Created.Should().Contain("DisabledMod");
        installOutput.SyncMods.Created.Should().NotContain("[NoDelete]UserMod");
        installOutput.SyncMods.Skipped.Should().Contain(l => l.StartsWith("#"));

        installOutput.GenerateMetaIni.Written.Should().Contain("TestMod");

        // 12.9: extensions/extras в манифесте пустые → шаги — no-op.
        installOutput.ExecuteExtensions.Written.Should().BeEmpty();
        installOutput.ExecuteExtensions.Skipped.Should().BeEmpty();
        installOutput.ExecuteExtras.Written.Should().BeEmpty();
        installOutput.ExecuteExtras.Skipped.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Идемпотентность installer-а
    // ------------------------------------------------------------------

    [Fact]
    public async Task Install_Twice_SecondRunIsIdempotent()
    {
        var (mo2Hash, _, _) = BuildSyntheticInstance();
        var configPath = WritePackConfig(mo2Hash);

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 };

        var packPipeline = BuildPackPipeline();
        var packResult = await packPipeline.ExecuteAsync(
            PackInputFactory.Create(configPath, parallelOptions),
            CancellationToken.None);

        PreloadTargetDownloads();

        var manifestPath = packResult.ManifestPath;
        var installPipeline = BuildInstallPipeline();

        var input = InstallInputFactory.Create(
            manifestPath,
            target: _targetDir,
            parallelOptions: parallelOptions);

        var first = await installPipeline.ExecuteAsync(input, CancellationToken.None);
        var modlistPath = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "modlist.txt");
        var modlistAfterFirst = File.ReadAllText(modlistPath);

        var second = await installPipeline.ExecuteAsync(input, CancellationToken.None);

        second.SyncMods.Created.Should().BeEmpty();
        second.SyncMods.Recreated.Should().BeEmpty();
        second.SyncMods.Deleted.Should().BeEmpty();
        second.SyncMods.Skipped.Should().Contain("TestMod");
        second.SyncMods.Skipped.Should().Contain("DisabledMod");

        var modlistAfterSecond = File.ReadAllText(modlistPath);
        modlistAfterSecond.Should().Be(modlistAfterFirst);
    }
}
