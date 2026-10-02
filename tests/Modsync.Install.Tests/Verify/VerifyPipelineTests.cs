// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install.Verify;
using Modsync.Platform.MO2.Writers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests.Verify;

public class VerifyPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _targetDir;
    private readonly FileHashCache _hashCache = new();
    private readonly VerifyPipeline _pipeline;

    public VerifyPipelineTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-verify-" + Guid.NewGuid());
        _targetDir = Path.Combine(_tempDir, "instance");
        Directory.CreateDirectory(_targetDir);

        _pipeline = new VerifyPipeline(
            _hashCache,
            NullLogger<VerifyPipeline>.Instance);
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

    private static readonly byte[] Mo2ExeContent =
        Encoding.UTF8.GetBytes("fake MO2 exe");

    private static readonly byte[] ModFileContent =
        Encoding.UTF8.GetBytes("hello from mod");

    private static readonly byte[] ExtDllContent =
        Encoding.UTF8.GetBytes("fake ext dll content");

    private static readonly byte[] SkseLoaderContent =
        Encoding.UTF8.GetBytes("fake skse loader content");

    private static readonly ModMeta TestModMeta = new()
    {
        GameName = "Skyrim Special Edition",
        GameId = "skyrimspecialedition",
        ModId = 3863,
        FileId = 1000172397,
        Version = "5.1",
        Repository = "Nexus",
        Url = "https://www.nexusmods.com/skyrimspecialedition/mods/3863",
        Comments = "",
        Notes = "test note",
    };

    // ------------------------------------------------------------------
    //  Хелперы построения инстанса
    // ------------------------------------------------------------------

    private TestInstance BuildValidInstance(
        bool withMetaIni = false,
        bool withExtensions = false,
        bool withExtras = false)
    {
        var mo2Path = Path.Combine(_targetDir, "MO2");
        var stockGamePath = Path.Combine(_targetDir, "Stock Game");
        var downloadsPath = Path.Combine(mo2Path, "downloads");
        var modsPath = Path.Combine(mo2Path, "mods");
        var profilesPath = Path.Combine(mo2Path, "profiles");
        var profileDir = Path.Combine(profilesPath, ProfileName);
        var modDir = Path.Combine(modsPath, ModName);

        Directory.CreateDirectory(mo2Path);
        Directory.CreateDirectory(stockGamePath);
        Directory.CreateDirectory(downloadsPath);
        Directory.CreateDirectory(modDir);
        Directory.CreateDirectory(profileDir);

        // MO2 exe
        File.WriteAllBytes(
            Path.Combine(mo2Path, "ModOrganizer.exe"), Mo2ExeContent);

        // MO2 архив: zip с ModOrganizer.exe
        var mo2ArchivePath = Path.Combine(downloadsPath, Mo2ArchiveName);
        using (var fs = File.Create(mo2ArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("ModOrganizer.exe");
            using var es = entry.Open();
            es.Write(Mo2ExeContent, 0, Mo2ExeContent.Length);
        }
        var mo2ArchiveHash = _hashCache.GetOrCompute(mo2ArchivePath);
        var mo2ArchiveSize = new FileInfo(mo2ArchivePath).Length;

        // Mod архив: zip с file.txt (+ опционально ext.dll, skse64_loader.exe)
        var modArchivePath = Path.Combine(downloadsPath, ModArchiveName);
        using (var fs = File.Create(modArchivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("file.txt");
            using (var s = e1.Open())
                s.Write(ModFileContent, 0, ModFileContent.Length);

            if (withExtensions)
            {
                var e2 = zip.CreateEntry("plugins/ext.dll");
                using (var s = e2.Open())
                    s.Write(ExtDllContent, 0, ExtDllContent.Length);
            }

            if (withExtras)
            {
                var e3 = zip.CreateEntry("skse64_loader.exe");
                using (var s = e3.Open())
                    s.Write(SkseLoaderContent, 0, SkseLoaderContent.Length);
            }
        }
        var modArchiveHash = _hashCache.GetOrCompute(modArchivePath);
        var modArchiveSize = new FileInfo(modArchivePath).Length;

        // Файл мода
        File.WriteAllBytes(Path.Combine(modDir, "file.txt"), ModFileContent);
        var modFileHash = XxHash64Value.FromStream(new MemoryStream(ModFileContent));
        var modFileSize = ModFileContent.Length;

        // meta.ini — только если попросили.
        if (withMetaIni)
        {
            MetaIniWriter.WriteFile(
                Path.Combine(modDir, "meta.ini"), TestModMeta);
        }

        // Extensions на диске
        if (withExtensions)
        {
            var extDir = Path.Combine(mo2Path, "plugins");
            Directory.CreateDirectory(extDir);
            File.WriteAllBytes(
                Path.Combine(extDir, "ext.dll"), ExtDllContent);
        }

        // Extras на диске
        if (withExtras)
        {
            File.WriteAllBytes(
                Path.Combine(stockGamePath, "skse64_loader.exe"),
                SkseLoaderContent);
        }

        // Профиль
        ModlistWriter.WriteFile(
            Path.Combine(profileDir, "modlist.txt"),
            new Modsync.Core.Models.Mo2.ModlistFile
            {
                Entries = new[]
                {
                    new Modsync.Core.Models.Mo2.ModlistEntry(ModName, true),
                },
            });

        PluginsWriter.WriteFile(
            Path.Combine(profileDir, "plugins.txt"),
            new Modsync.Core.Models.Mo2.PluginsFile
            {
                Entries = new[]
                {
                    new Modsync.Core.Models.Mo2.PluginEntry("Test.esp", true),
                },
            });

        LoadorderWriter.WriteFile(
            Path.Combine(profileDir, "loadorder.txt"),
            new Modsync.Core.Models.Mo2.LoadorderFile
            {
                Plugins = new[] { "Skyrim.esm", "Test.esp" },
            });

        // Extensions в манифесте
        IReadOnlyList<ExtensionEntry> extensions =
            Array.Empty<ExtensionEntry>();
        if (withExtensions)
        {
            var extFileHash = XxHash64Value.FromStream(
                new MemoryStream(ExtDllContent));
            extensions = new[]
            {
                new ExtensionEntry
                {
                    Name = "plugins/ext.dll",
                    Directives = new Directive[]
                    {
                        new FromArchiveDirective
                        {
                            Archive = "local_testmod",
                            Source = "plugins/ext.dll",
                            Destination = "plugins/ext.dll",
                            Hash = extFileHash,
                            Size = ExtDllContent.Length,
                        },
                    },
                },
            };
        }

        // Extras в манифесте
        IReadOnlyList<ExtensionEntry> extras =
            Array.Empty<ExtensionEntry>();
        if (withExtras)
        {
            var skseHash = XxHash64Value.FromStream(
                new MemoryStream(SkseLoaderContent));
            extras = new[]
            {
                new ExtensionEntry
                {
                    Name = "skse64_loader.exe",
                    Directives = new Directive[]
                    {
                        new FromArchiveDirective
                        {
                            Archive = "local_testmod",
                            Source = "skse64_loader.exe",
                            Destination = "skse64_loader.exe",
                            Hash = skseHash,
                            Size = SkseLoaderContent.Length,
                        },
                    },
                },
            };
        }

        // Манифест
        var manifest = new ModlistManifest
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test Pack",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Execution = new ExecutionPolicy(),
            Mo2 = new Mo2Section
            {
                Version = "2.5.2",
                Profile = ProfileName,
                Archive = new ArchiveEntry
                {
                    Id = "local_mod-organizer-2-5-2",
                    Name = Mo2ArchiveName,
                    Size = mo2ArchiveSize,
                    Hash = mo2ArchiveHash,
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/" + Mo2ArchiveName,
                            Hash = mo2ArchiveHash,
                        },
                    },
                },
                Extensions = extensions,
            },
            StockGame = new StockGameSection
            {
                Extras = extras,
            },
            Archives = new[]
            {
                new ArchiveEntry
                {
                    Id = "local_testmod",
                    Name = ModArchiveName,
                    Size = modArchiveSize,
                    Hash = modArchiveHash,
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/" + ModArchiveName,
                            Hash = modArchiveHash,
                        },
                    },
                },
            },
            Mods = new[]
            {
                new ModEntry
                {
                    Name = ModName,
                    Enabled = true,
                    Order = 0,
                    Meta = withMetaIni ? TestModMeta : null,
                    Directives = new Directive[]
                    {
                        new FromArchiveDirective
                        {
                            Archive = "local_testmod",
                            Source = "file.txt",
                            Destination = "file.txt",
                            Hash = modFileHash,
                            Size = modFileSize,
                        },
                    },
                },
            },
            Plugins = new[]
            {
                new PluginEntry { Name = "Test.esp", Enabled = true, Order = 0 },
            },
            Loadorder = new[] { "Skyrim.esm", "Test.esp" },
        };

        ManifestJson.Save(
            Path.Combine(_targetDir, "modlist.json"),
            manifest);

        return new TestInstance
        {
            Target = _targetDir,
            Mo2Path = mo2Path,
            DownloadsPath = downloadsPath,
            ModsPath = modsPath,
            ProfilesPath = profilesPath,
            ProfileDir = profileDir,
            StockGamePath = stockGamePath,
            ModDir = modDir,
            Manifest = manifest,
        };
    }

    private sealed class TestInstance
    {
        public required string Target { get; init; }
        public required string Mo2Path { get; init; }
        public required string DownloadsPath { get; init; }
        public required string ModsPath { get; init; }
        public required string ProfilesPath { get; init; }
        public required string ProfileDir { get; init; }
        public required string StockGamePath { get; init; }
        public required string ModDir { get; init; }
        public required ModlistManifest Manifest { get; init; }
    }

    private VerifyReport Run() =>
        _pipeline.Execute(_targetDir, CancellationToken.None);

    private static bool HasFailure(VerifyReport r, string namePart)
        => r.Failures.Any(f => f.Name.Contains(namePart));

    // ------------------------------------------------------------------
    //  1. Happy path
    // ------------------------------------------------------------------

    [Fact]
    public void HappyPath_ReturnsAllChecksPassed()
    {
        BuildValidInstance();
        var report = Run();

        report.IsOk.Should().BeTrue(
            "failures: " + string.Join("; ",
                report.Failures.Select(f => f.Name + ": " + f.Message)));
        report.FailedCount.Should().Be(0);
        report.PassedCount.Should().BeGreaterThan(10);
    }

    [Fact]
    public void HappyPath_WithMetaIni_AllChecksPassed()
    {
        BuildValidInstance(withMetaIni: true);
        var report = Run();

        report.IsOk.Should().BeTrue();
        report.FailedCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  2–6. Manifest и структура
    // ------------------------------------------------------------------

    [Fact]
    public void TargetDoesNotExist_Fails()
    {
        Directory.Delete(_targetDir, recursive: true);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Target directory exists").Should().BeTrue();
    }

    [Fact]
    public void ManifestDoesNotExist_Fails()
    {
        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Manifest exists").Should().BeTrue();
    }

    [Fact]
    public void ManifestMalformed_Fails()
    {
        File.WriteAllText(
            Path.Combine(_targetDir, "modlist.json"),
            "{ this is not json }");

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Manifest parses").Should().BeTrue();
    }

    [Fact]
    public void UnsupportedSchemaVersion_Fails()
    {
        var inst = BuildValidInstance();

        var badManifest = CloneManifestWithSchemaVersion(
            inst.Manifest, "99.0.0");
        ManifestJson.Save(
            Path.Combine(_targetDir, "modlist.json"),
            badManifest);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "schemaVersion supported").Should().BeTrue();
    }

    [Fact]
    public void InvalidMetaName_Fails()
    {
        var inst = BuildValidInstance();

        var badManifest = CloneManifestWithName(inst.Manifest, "CON");
        ManifestJson.Save(
            Path.Combine(_targetDir, "modlist.json"),
            badManifest);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "meta.name valid").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  7–10. Структура инстанса
    // ------------------------------------------------------------------

    [Fact]
    public void MissingMo2Folder_Fails()
    {
        var inst = BuildValidInstance();
        Directory.Delete(inst.Mo2Path, recursive: true);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "MO2/ exists").Should().BeTrue();
    }

    [Fact]
    public void MissingStockGameFolder_Fails()
    {
        var inst = BuildValidInstance();
        Directory.Delete(inst.StockGamePath, recursive: true);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Stock Game/ exists").Should().BeTrue();
    }

    [Fact]
    public void MissingDownloadsFolder_Fails()
    {
        var inst = BuildValidInstance();
        Directory.Delete(inst.DownloadsPath, recursive: true);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "MO2/downloads/ exists").Should().BeTrue();
    }

    [Fact]
    public void MissingModOrganizerExe_Fails()
    {
        var inst = BuildValidInstance();
        File.Delete(Path.Combine(inst.Mo2Path, "ModOrganizer.exe"));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "ModOrganizer.exe exists").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  11–12. Архивы в downloads/
    // ------------------------------------------------------------------

    [Fact]
    public void MissingMo2ArchiveInDownloads_Fails()
    {
        var inst = BuildValidInstance();
        File.Delete(Path.Combine(inst.DownloadsPath, Mo2ArchiveName));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "MO2 archive").Should().BeTrue();
    }

    [Fact]
    public void MissingModArchiveInDownloads_Fails()
    {
        var inst = BuildValidInstance();
        File.Delete(Path.Combine(inst.DownloadsPath, ModArchiveName));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, $"Archive: {ModArchiveName}").Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  13–15. Файлы модов
    // ------------------------------------------------------------------

    [Fact]
    public void MissingModFile_Fails()
    {
        var inst = BuildValidInstance();
        File.Delete(Path.Combine(inst.ModDir, "file.txt"));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "TestMod / file.txt").Should().BeTrue();
    }

    [Fact]
    public void ModFileWrongSize_Fails()
    {
        var inst = BuildValidInstance();
        File.WriteAllText(
            Path.Combine(inst.ModDir, "file.txt"),
            "much longer content than original");

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "TestMod / file.txt").Should().BeTrue();
        report.Failures.Should().Contain(f =>
            f.Message != null && f.Message.Contains("Size mismatch"));
    }

    [Fact]
    public void ModFileWrongHash_Fails()
    {
        var inst = BuildValidInstance();
        var original = ModFileContent;
        var fake = new byte[original.Length];
        for (int i = 0; i < original.Length; i++)
            fake[i] = (byte)(original[i] ^ 0xFF);
        File.WriteAllBytes(Path.Combine(inst.ModDir, "file.txt"), fake);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "TestMod / file.txt").Should().BeTrue();
        report.Failures.Should().Contain(f =>
            f.Message != null && f.Message.Contains("Hash mismatch"));
    }

    // ------------------------------------------------------------------
    //  16–17. Профиль
    // ------------------------------------------------------------------

    [Fact]
    public void ModlistTxtMismatch_Fails()
    {
        var inst = BuildValidInstance();
        File.WriteAllText(
            Path.Combine(inst.ProfileDir, "modlist.txt"),
            "\uFEFF# This file was automatically generated by Mod Organizer.\r\n" +
            "+OtherMod\r\n");

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Profile: modlist.txt").Should().BeTrue();
    }

    [Fact]
    public void ModlistTxtWithBom_Passes()
    {
        var inst = BuildValidInstance();
        var path = Path.Combine(inst.ProfileDir, "modlist.txt");
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        File.WriteAllText(path, text);

        var report = Run();

        HasFailure(report, "Profile: modlist.txt").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  18–19. Особые случаи модов
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyDirectivesForDisabledMod_Passes()
    {
        var inst = BuildValidInstance();

        var badManifest = CloneManifestWithMods(inst.Manifest, new[]
        {
            new ModEntry
            {
                Name = "EmptyMod",
                Enabled = false,
                Order = 0,
                Meta = null,
                Directives = Array.Empty<Directive>(),
            },
        });
        ManifestJson.Save(
            Path.Combine(_targetDir, "modlist.json"),
            badManifest);

        Directory.CreateDirectory(Path.Combine(inst.ModsPath, "EmptyMod"));

        var report = Run();

        HasFailure(report, "Mod: EmptyMod").Should().BeFalse();
    }

    [Fact]
    public void Separator_Passes()
    {
        var inst = BuildValidInstance();

        var badManifest = CloneManifestWithMods(inst.Manifest, new[]
        {
            new ModEntry
            {
                Name = "# separator",
                Enabled = false,
                Order = 0,
                Meta = null,
                Directives = Array.Empty<Directive>(),
            },
        });
        ManifestJson.Save(
            Path.Combine(_targetDir, "modlist.json"),
            badManifest);

        var report = Run();

        HasFailure(report, "Mod: # separator").Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  20–30. meta.ini (12.11.5)
    // ------------------------------------------------------------------

    [Fact]
    public void MetaIni_HappyPath_Match()
    {
        BuildValidInstance(withMetaIni: true);
        var report = Run();

        HasFailure(report, "TestMod / meta.ini").Should().BeFalse();
    }

    [Fact]
    public void MetaIni_Missing_Fails()
    {
        var inst = BuildValidInstance(withMetaIni: true);
        File.Delete(Path.Combine(inst.ModDir, "meta.ini"));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "TestMod / meta.ini").Should().BeTrue();
    }

    [Fact]
    public void MetaIni_ModIdDiffers_Fails()
    {
        var inst = BuildValidInstance(withMetaIni: true);

        var badMeta = TestModMeta with { ModId = 9999 };
        MetaIniWriter.WriteFile(Path.Combine(inst.ModDir, "meta.ini"), badMeta);

        var report = Run();

        report.IsOk.Should().BeFalse();
        var failure = report.Failures
            .FirstOrDefault(f => f.Name == "Mod: TestMod / meta.ini");
        failure.Should().NotBeNull();
        failure!.Message.Should().Contain("modID");
    }

    [Fact]
    public void MetaIni_FileIdDiffers_Fails()
    {
        var inst = BuildValidInstance(withMetaIni: true);

        var badMeta = TestModMeta with { FileId = 42 };
        MetaIniWriter.WriteFile(Path.Combine(inst.ModDir, "meta.ini"), badMeta);

        var report = Run();

        report.IsOk.Should().BeFalse();
        var failure = report.Failures
            .FirstOrDefault(f => f.Name == "Mod: TestMod / meta.ini");
        failure!.Message.Should().Contain("fileID");
    }

    [Fact]
    public void MetaIni_VersionDiffers_Fails()
    {
        var inst = BuildValidInstance(withMetaIni: true);

        var badMeta = TestModMeta with { Version = "6.0" };
        MetaIniWriter.WriteFile(Path.Combine(inst.ModDir, "meta.ini"), badMeta);

        var report = Run();

        report.IsOk.Should().BeFalse();
        var failure = report.Failures
            .FirstOrDefault(f => f.Name == "Mod: TestMod / meta.ini");
        failure!.Message.Should().Contain("version");
    }

    [Fact]
    public void MetaIni_NotesDiffer_Fails()
    {
        var inst = BuildValidInstance(withMetaIni: true);

        var badMeta = TestModMeta with { Notes = "different note" };
        MetaIniWriter.WriteFile(Path.Combine(inst.ModDir, "meta.ini"), badMeta);

        var report = Run();

        report.IsOk.Should().BeFalse();
        var failure = report.Failures
            .FirstOrDefault(f => f.Name == "Mod: TestMod / meta.ini");
        failure!.Message.Should().Contain("notes");
    }

    [Fact]
    public void MetaIni_NullVsEmptyString_Passes()
    {
        var inst = BuildValidInstance();
        var manifestWithNull = CloneManifestWithMods(inst.Manifest, new[]
        {
            new ModEntry
            {
                Name = "TestMod",
                Enabled = true,
                Order = 0,
                Meta = new ModMeta
                {
                    ModId = 3863,
                    FileId = 1000172397,
                    Comments = null,
                    Notes = null,
                },
                Directives = inst.Manifest.Mods[0].Directives,
            },
        });
        ManifestJson.Save(
            Path.Combine(_targetDir, "modlist.json"),
            manifestWithNull);

        var actualMeta = new ModMeta
        {
            ModId = 3863,
            FileId = 1000172397,
            Comments = "",
            Notes = "",
        };
        MetaIniWriter.WriteFile(Path.Combine(inst.ModDir, "meta.ini"), actualMeta);

        var report = Run();

        HasFailure(report, "TestMod / meta.ini").Should().BeFalse();
    }

    [Fact]
    public void MetaIni_NullInManifest_FileExists_Fails()
    {
        var inst = BuildValidInstance();
        MetaIniWriter.WriteFile(
            Path.Combine(inst.ModDir, "meta.ini"), TestModMeta);

        var report = Run();

        report.IsOk.Should().BeFalse();
        var failure = report.Failures
            .FirstOrDefault(f => f.Name == "Mod: TestMod / meta.ini");
        failure.Should().NotBeNull();
        failure!.Message.Should().Contain("no meta");
    }

    [Fact]
    public void MetaIni_NullInManifest_FileAbsent_Passes()
    {
        BuildValidInstance();
        var report = Run();

        HasFailure(report, "TestMod / meta.ini").Should().BeFalse();
    }

    [Fact]
    public void MetaIni_Unparseable_Fails()
    {
        var inst = BuildValidInstance(withMetaIni: true);

        File.WriteAllText(
            Path.Combine(inst.ModDir, "meta.ini"),
            "; not a valid ini\r\n[installedFiles]\r\n1\\foo.txt=DEADBEEF\r\n");

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "TestMod / meta.ini").Should().BeTrue();
    }

    [Fact]
    public void MetaIni_MultipleFieldsDiffer_ReportsAll()
    {
        var inst = BuildValidInstance(withMetaIni: true);

        var badMeta = TestModMeta with
        {
            ModId = 9999,
            FileId = 42,
            Version = "6.0",
        };
        MetaIniWriter.WriteFile(Path.Combine(inst.ModDir, "meta.ini"), badMeta);

        var report = Run();

        var failure = report.Failures
            .FirstOrDefault(f => f.Name == "Mod: TestMod / meta.ini");
        failure.Should().NotBeNull();
        failure!.Message.Should().Contain("modID");
        failure.Message.Should().Contain("fileID");
        failure.Message.Should().Contain("version");
    }

    // ------------------------------------------------------------------
    //  31–40. Extensions / extras (12.11.6)
    // ------------------------------------------------------------------

    [Fact]
    public void Extensions_HappyPath_Passes()
    {
        BuildValidInstance(withExtensions: true);
        var report = Run();

        report.IsOk.Should().BeTrue(
            "failures: " + string.Join("; ",
                report.Failures.Select(f => f.Name + ": " + f.Message)));
        HasFailure(report, "MO2 extension: plugins/ext.dll").Should().BeFalse();
    }

    [Fact]
    public void Extensions_FileMissing_Fails()
    {
        var inst = BuildValidInstance(withExtensions: true);
        File.Delete(Path.Combine(inst.Mo2Path, "plugins", "ext.dll"));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "MO2 extension: plugins/ext.dll").Should().BeTrue();
    }

    [Fact]
    public void Extensions_FileWrongHash_Fails()
    {
        var inst = BuildValidInstance(withExtensions: true);
        var original = ExtDllContent;
        var fake = new byte[original.Length];
        for (int i = 0; i < original.Length; i++)
            fake[i] = (byte)(original[i] ^ 0xFF);
        File.WriteAllBytes(
            Path.Combine(inst.Mo2Path, "plugins", "ext.dll"), fake);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "MO2 extension: plugins/ext.dll").Should().BeTrue();
        report.Failures.Should().Contain(f =>
            f.Name.Contains("MO2 extension") &&
            f.Message != null && f.Message.Contains("Hash mismatch"));
    }

    [Fact]
    public void Extensions_FileWrongSize_Fails()
    {
        var inst = BuildValidInstance(withExtensions: true);
        File.WriteAllText(
            Path.Combine(inst.Mo2Path, "plugins", "ext.dll"),
            "much longer content than original ext dll");

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "MO2 extension: plugins/ext.dll").Should().BeTrue();
    }

    [Fact]
    public void Extras_HappyPath_Passes()
    {
        BuildValidInstance(withExtras: true);
        var report = Run();

        report.IsOk.Should().BeTrue(
            "failures: " + string.Join("; ",
                report.Failures.Select(f => f.Name + ": " + f.Message)));
        HasFailure(report, "Stock Game extra: skse64_loader.exe").Should().BeFalse();
    }

    [Fact]
    public void Extras_FileMissing_Fails()
    {
        var inst = BuildValidInstance(withExtras: true);
        File.Delete(Path.Combine(inst.StockGamePath, "skse64_loader.exe"));

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Stock Game extra: skse64_loader.exe").Should().BeTrue();
    }

    [Fact]
    public void Extras_FileWrongHash_Fails()
    {
        var inst = BuildValidInstance(withExtras: true);
        var original = SkseLoaderContent;
        var fake = new byte[original.Length];
        for (int i = 0; i < original.Length; i++)
            fake[i] = (byte)(original[i] ^ 0xFF);
        File.WriteAllBytes(
            Path.Combine(inst.StockGamePath, "skse64_loader.exe"), fake);

        var report = Run();

        report.IsOk.Should().BeFalse();
        HasFailure(report, "Stock Game extra: skse64_loader.exe").Should().BeTrue();
    }

    [Fact]
    public void Both_ExtensionsAndExtras_AllChecked()
    {
        BuildValidInstance(withExtensions: true, withExtras: true);
        var report = Run();

        report.IsOk.Should().BeTrue(
            "failures: " + string.Join("; ",
                report.Failures.Select(f => f.Name + ": " + f.Message)));
        HasFailure(report, "MO2 extension: plugins/ext.dll").Should().BeFalse();
        HasFailure(report, "Stock Game extra: skse64_loader.exe").Should().BeFalse();
    }

    [Fact]
    public void EmptyExtensions_EmptyExtras_NoExtraChecks()
    {
        // Sanity: базовый инстанс без extensions/extras — просто happy path.
        BuildValidInstance();
        var report = Run();

        report.IsOk.Should().BeTrue();
        // Никаких проверок с "MO2 extension" / "Stock Game extra".
        report.Checks.Should().NotContain(c =>
            c.Name.StartsWith("MO2 extension") ||
            c.Name.StartsWith("Stock Game extra"));
    }

    // ------------------------------------------------------------------
    //  Clone helpers
    // ------------------------------------------------------------------

    private static ModlistManifest CloneManifestWithSchemaVersion(
        ModlistManifest source, string schemaVersion)
        => new()
        {
            SchemaVersion = schemaVersion,
            ManifestVersion = source.ManifestVersion,
            CreatedAt = source.CreatedAt,
            CreatedBy = source.CreatedBy,
            Meta = source.Meta,
            Execution = source.Execution,
            Mo2 = source.Mo2,
            StockGame = source.StockGame,
            Archives = source.Archives,
            Mods = source.Mods,
            Plugins = source.Plugins,
            Loadorder = source.Loadorder,
        };

    private static ModlistManifest CloneManifestWithName(
        ModlistManifest source, string name)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            ManifestVersion = source.ManifestVersion,
            CreatedAt = source.CreatedAt,
            CreatedBy = source.CreatedBy,
            Meta = new ManifestMeta
            {
                Name = name,
                Version = source.Meta.Version,
                Author = source.Meta.Author,
                Game = source.Meta.Game,
                GameVersion = source.Meta.GameVersion,
            },
            Execution = source.Execution,
            Mo2 = source.Mo2,
            StockGame = source.StockGame,
            Archives = source.Archives,
            Mods = source.Mods,
            Plugins = source.Plugins,
            Loadorder = source.Loadorder,
        };

    private static ModlistManifest CloneManifestWithMods(
        ModlistManifest source, IReadOnlyList<ModEntry> mods)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            ManifestVersion = source.ManifestVersion,
            CreatedAt = source.CreatedAt,
            CreatedBy = source.CreatedBy,
            Meta = source.Meta,
            Execution = source.Execution,
            Mo2 = source.Mo2,
            StockGame = source.StockGame,
            Archives = source.Archives,
            Mods = mods,
            Plugins = source.Plugins,
            Loadorder = source.Loadorder,
        };
}
