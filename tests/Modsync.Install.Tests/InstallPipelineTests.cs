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
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Install;
using Modsync.Install.Downloaders;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class InstallPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _targetDir;
    private readonly FileHashCache _hashCache = new();

    public InstallPipelineTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-pipe-" + Guid.NewGuid());
        _sourceDir = Path.Combine(_tempDir, "source");
        _targetDir = Path.Combine(_tempDir, "target");

        Directory.CreateDirectory(_sourceDir);
        // _targetDir НЕ создаём — pipeline должен сам.
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы: создание архивов
    // ------------------------------------------------------------------

    private (string name, XxHash64Value hash, long size)
        CreateMo2Archive(
            string downloadsDir,
            string fileName = "Mod.Organizer-2.5.2.7z")
    {
        Directory.CreateDirectory(downloadsDir);

        var archivePath = Path.Combine(downloadsDir, fileName);
        var content = Encoding.UTF8.GetBytes("fake mo2 exe content");

        using (var fs = File.Create(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("ModOrganizer.exe");
            using var es = entry.Open();
            es.Write(content, 0, content.Length);
        }

        var hash = _hashCache.GetOrCompute(archivePath);
        var size = new FileInfo(archivePath).Length;
        return (fileName, hash, size);
    }

    /// <summary>
    /// Создаёт mod-архив с произвольным набором файлов.
    /// Возвращает:
    ///   - name/archiveHash/archiveSize — для ArchiveEntry;
    ///   - fileHashes — словарь "путь в архиве" → (hash, size) содержимого,
    ///     чтобы в тестах брать нужные хеши для директив.
    /// </summary>
    private (
        string name,
        XxHash64Value archiveHash,
        long archiveSize,
        IReadOnlyDictionary<string, (XxHash64Value hash, long size)> fileHashes)
        CreateModArchive(
            string downloadsDir,
            string fileName,
            params (string path, string content)[] files)
    {
        if (files.Length == 0)
            files = new[] { ("file.txt", "hello from mod") };

        Directory.CreateDirectory(downloadsDir);

        var archivePath = Path.Combine(downloadsDir, fileName);

        using (var fs = File.Create(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (path, content) in files)
            {
                var entry = zip.CreateEntry(path);
                using var es = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                es.Write(bytes, 0, bytes.Length);
            }
        }

        var archiveHash = _hashCache.GetOrCompute(archivePath);
        var archiveSize = new FileInfo(archivePath).Length;

        var fileHashes = new Dictionary<string, (XxHash64Value, long)>(
            StringComparer.Ordinal);

        foreach (var (path, content) in files)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var hash = XxHash64Value.FromStream(new MemoryStream(bytes));
            fileHashes[path] = (hash, bytes.Length);
        }

        return (fileName, archiveHash, archiveSize, fileHashes);
    }

    // ------------------------------------------------------------------
    //  Хелперы: манифест
    // ------------------------------------------------------------------

    private string WriteManifest(ModlistManifest manifest)
    {
        var path = Path.Combine(_sourceDir, "modlist.json");
        File.WriteAllText(path, ManifestJson.Serialize(manifest));
        return path;
    }

    private InstallPipeline BuildPipeline()
    {
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
                registry, _hashCache, extractor,
                NullLogger<BootstrapMo2Step>.Instance),
            new SyncArchivesStep(
                registry, _hashCache,
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
                extractor, _hashCache,
                NullLogger<SyncModsStep>.Instance),
            new GenerateMetaIniStep(
                NullLogger<GenerateMetaIniStep>.Instance),
            new RegenerateProfileStep(
                NullLogger<RegenerateProfileStep>.Instance),
            NullLogger<InstallPipeline>.Instance);
    }

    private ModlistManifest MakeManifest(
        string mo2ArchiveName, XxHash64Value mo2Hash, long mo2Size,
        string modArchiveName, XxHash64Value modArchiveHash, long modArchiveSize,
        XxHash64Value modFileHash, long modFileSize,
        bool withExtensions = false,
        bool withExtras = false,
        XxHash64Value? extFileHash = null,
        long extFileSize = 0,
        XxHash64Value? extraFileHash = null,
        long extraFileSize = 0)
    {
        var modArchive = new ArchiveEntry
        {
            Id = "local_testmod",
            Name = modArchiveName,
            Size = modArchiveSize,
            Hash = modArchiveHash,
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef
                {
                    Url = "https://example.com/TestMod.7z",
                    Hash = modArchiveHash,
                },
            },
        };

        var mod = new ModEntry
        {
            Name = "TestMod",
            Enabled = true,
            Order = 0,
            Meta = new ModMeta
            {
                ModId = 12345,
                FileId = 67890,
                Version = "1.0.0",
                Notes = "test note",
            },
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
        };

        IReadOnlyList<ExtensionEntry> extensions = Array.Empty<ExtensionEntry>();
        if (withExtensions)
        {
            if (extFileHash is null)
                throw new InvalidOperationException(
                    "withExtensions = true, but extFileHash is null.");

            extensions = new[]
            {
                new ExtensionEntry
                {
                    Name = "fake-ext",
                    Directives = new Directive[]
                    {
                        new FromArchiveDirective
                        {
                            Archive = "local_testmod",
                            Source = "plugins/ext.dll",
                            Destination = "plugins/ext.dll",
                            Hash = extFileHash.Value,
                            Size = extFileSize,
                        },
                    },
                },
            };
        }

        IReadOnlyList<ExtensionEntry> extras = Array.Empty<ExtensionEntry>();
        if (withExtras)
        {
            if (extraFileHash is null)
                throw new InvalidOperationException(
                    "withExtras = true, but extraFileHash is null.");

            extras = new[]
            {
                new ExtensionEntry
                {
                    Name = "fake-extra",
                    Directives = new Directive[]
                    {
                        new FromArchiveDirective
                        {
                            Archive = "local_testmod",
                            Source = "extra.exe",
                            Destination = "extra.exe",
                            Hash = extraFileHash.Value,
                            Size = extraFileSize,
                        },
                    },
                },
            };
        }

        return new ModlistManifest
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
                Profile = "Default",
                Archive = new ArchiveEntry
                {
                    Id = "mo2",
                    Name = mo2ArchiveName,
                    Size = mo2Size,
                    Hash = mo2Hash,
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                            Hash = mo2Hash,
                        },
                    },
                },
                Extensions = extensions,
            },
            StockGame = new StockGameSection { Extras = extras },
            Archives = new[] { modArchive },
            Mods = new[] { mod },
            Plugins = new[]
            {
                new PluginEntry { Name = "Skyrim.esm", Enabled = true, Order = 0 },
            },
            Loadorder = new[] { "Skyrim.esm" },
        };
    }

    private InstallPipeline.Input MakeInput(string manifestPath)
        => new()
        {
            ManifestPath = manifestPath,
            Target = _targetDir,
            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
        };

    // ------------------------------------------------------------------
    //  Тест 1: Happy path (без extensions/extras)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FullLocalFlow_ProducesValidInstance()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");

        var (mo2Name, mo2Hash, mo2Size) =
            CreateMo2Archive(downloadsDir);
        var (modName, modArchiveHash, modArchiveSize, fileHashes) =
            CreateModArchive(downloadsDir, "TestMod.7z");
        var (modFileHash, modFileSize) = fileHashes["file.txt"];

        var manifest = MakeManifest(
            mo2Name, mo2Hash, mo2Size,
            modName, modArchiveHash, modArchiveSize,
            modFileHash, modFileSize);
        var manifestPath = WriteManifest(manifest);

        var pipeline = BuildPipeline();
        var output = await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);

        // --- Инстанс ---
        output.InstancePath.Should().Be(_targetDir);
        Directory.Exists(_targetDir).Should().BeTrue();

        // --- MO2 распакован ---
        File.Exists(Path.Combine(_targetDir, "MO2", "ModOrganizer.exe"))
            .Should().BeTrue();

        // --- Мод разложен ---
        var modFile = Path.Combine(
            _targetDir, "MO2", "mods", "TestMod", "file.txt");
        File.Exists(modFile).Should().BeTrue();
        File.ReadAllText(modFile).Should().Be("hello from mod");

        // --- meta.ini ---
        var metaIni = Path.Combine(
            _targetDir, "MO2", "mods", "TestMod", "meta.ini");
        File.Exists(metaIni).Should().BeTrue();
        File.ReadAllText(metaIni).Should().Contain("modID=12345");
        File.ReadAllText(metaIni).Should().Contain("notes=test note");

        // --- Профиль ---
        var modlistTxt = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "modlist.txt");
        File.Exists(modlistTxt).Should().BeTrue();
        File.ReadAllText(modlistTxt).Should().Contain("+TestMod");

        var pluginsTxt = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "plugins.txt");
        File.Exists(pluginsTxt).Should().BeTrue();
        File.ReadAllText(pluginsTxt).Should().Contain("*Skyrim.esm");

        var loadorderTxt = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "loadorder.txt");
        File.Exists(loadorderTxt).Should().BeTrue();
        File.ReadAllText(loadorderTxt).Should().Contain("Skyrim.esm");

        // --- Копия манифеста в инстансе ---
        File.Exists(Path.Combine(_targetDir, "modlist.json")).Should().BeTrue();

        // --- Результаты шагов ---
        output.SyncArchives.AlreadyPresent.Should().Contain(modName);
        output.SyncArchives.Downloaded.Should().BeEmpty();
        output.SyncMods.Created.Should().ContainSingle().Which.Should().Be("TestMod");
        output.SyncMods.Recreated.Should().BeEmpty();
        output.SyncMods.Deleted.Should().BeEmpty();
        output.GenerateMetaIni.Written.Should().ContainSingle().Which.Should().Be("TestMod");
        output.GenerateMetaIni.Deleted.Should().BeEmpty();
        output.RegenerateProfile.ModlistCount.Should().Be(1);
        output.RegenerateProfile.PluginsCount.Should().Be(1);
        output.RegenerateProfile.LoadorderCount.Should().Be(1);

        // --- Extensions/extras пустые → no-op ---
        output.ExecuteExtensions.Written.Should().BeEmpty();
        output.ExecuteExtensions.Skipped.Should().BeEmpty();
        output.ExecuteExtras.Written.Should().BeEmpty();
        output.ExecuteExtras.Skipped.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Тест 2: Идемпотентность
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Twice_SecondRunIsIdempotent()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");

        var (mo2Name, mo2Hash, mo2Size) =
            CreateMo2Archive(downloadsDir);
        var (modName, modArchiveHash, modArchiveSize, fileHashes) =
            CreateModArchive(downloadsDir, "TestMod.7z");
        var (modFileHash, modFileSize) = fileHashes["file.txt"];

        var manifest = MakeManifest(
            mo2Name, mo2Hash, mo2Size,
            modName, modArchiveHash, modArchiveSize,
            modFileHash, modFileSize);
        var manifestPath = WriteManifest(manifest);

        var pipeline = BuildPipeline();

        var first = await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);
        first.SyncMods.Created.Should().ContainSingle();

        var modlistPath = Path.Combine(
            _targetDir, "MO2", "profiles", "Default", "modlist.txt");
        var afterFirst = File.ReadAllText(modlistPath);

        var second = await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);

        second.SyncArchives.AlreadyPresent.Should().Contain(modName);
        second.SyncArchives.Downloaded.Should().BeEmpty();
        second.SyncMods.Created.Should().BeEmpty();
        second.SyncMods.Recreated.Should().BeEmpty();
        second.SyncMods.Skipped.Should().Contain("TestMod");
        second.SyncMods.Deleted.Should().BeEmpty();

        var afterSecond = File.ReadAllText(modlistPath);
        afterSecond.Should().Be(afterFirst);
    }

    // ------------------------------------------------------------------
    //  Тест 3: Extensions непусты → файлы разложены в MO2/
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_NonEmptyExtensions_FilesWritten()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");

        var (mo2Name, mo2Hash, mo2Size) =
            CreateMo2Archive(downloadsDir);
        var (modName, modArchiveHash, modArchiveSize, fileHashes) =
            CreateModArchive(downloadsDir, "TestMod.7z",
                ("file.txt", "hello from mod"),
                ("plugins/ext.dll", "ext dll content"),
                ("extra.exe", "extra exe content"));

        var (modFileHash, modFileSize) = fileHashes["file.txt"];
        var (extFileHash, extFileSize) = fileHashes["plugins/ext.dll"];

        var manifest = MakeManifest(
            mo2Name, mo2Hash, mo2Size,
            modName, modArchiveHash, modArchiveSize,
            modFileHash, modFileSize,
            withExtensions: true,
            extFileHash: extFileHash,
            extFileSize: extFileSize);
        var manifestPath = WriteManifest(manifest);

        var pipeline = BuildPipeline();
        var output = await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);

        // Файл расширения разложен в <target>/MO2/plugins/ext.dll
        var extPath = Path.Combine(
            _targetDir, "MO2", "plugins", "ext.dll");
        File.Exists(extPath).Should().BeTrue();
        File.ReadAllText(extPath).Should().Be("ext dll content");

        output.ExecuteExtensions.Written.Should().ContainSingle()
            .Which.Should().Be("fake-ext");
        output.ExecuteExtensions.Skipped.Should().BeEmpty();

        // Extras пустые — no-op.
        output.ExecuteExtras.Written.Should().BeEmpty();
        output.ExecuteExtras.Skipped.Should().BeEmpty();

        // Mod разложен отдельно.
        File.Exists(Path.Combine(
            _targetDir, "MO2", "mods", "TestMod", "file.txt"))
            .Should().BeTrue();

        // Extra.exe НЕ должен быть разложен (extras пустые).
        File.Exists(Path.Combine(_targetDir, "Stock Game", "extra.exe"))
            .Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Тест 4: Extras непусты → файлы разложены в Stock Game/
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_NonEmptyExtras_FilesWritten()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");

        var (mo2Name, mo2Hash, mo2Size) =
            CreateMo2Archive(downloadsDir);
        var (modName, modArchiveHash, modArchiveSize, fileHashes) =
            CreateModArchive(downloadsDir, "TestMod.7z",
                ("file.txt", "hello from mod"),
                ("plugins/ext.dll", "ext dll content"),
                ("extra.exe", "extra exe content"));

        var (modFileHash, modFileSize) = fileHashes["file.txt"];
        var (extraFileHash, extraFileSize) = fileHashes["extra.exe"];

        var manifest = MakeManifest(
            mo2Name, mo2Hash, mo2Size,
            modName, modArchiveHash, modArchiveSize,
            modFileHash, modFileSize,
            withExtras: true,
            extraFileHash: extraFileHash,
            extraFileSize: extraFileSize);
        var manifestPath = WriteManifest(manifest);

        var pipeline = BuildPipeline();
        var output = await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);

        // Файл extras разложен в <target>/Stock Game/extra.exe
        var extraPath = Path.Combine(
            _targetDir, "Stock Game", "extra.exe");
        File.Exists(extraPath).Should().BeTrue();
        File.ReadAllText(extraPath).Should().Be("extra exe content");

        output.ExecuteExtras.Written.Should().ContainSingle()
            .Which.Should().Be("fake-extra");
        output.ExecuteExtras.Skipped.Should().BeEmpty();

        // Extensions пусты — no-op.
        output.ExecuteExtensions.Written.Should().BeEmpty();
        output.ExecuteExtensions.Skipped.Should().BeEmpty();

        // ext.dll НЕ должен быть разложен.
        File.Exists(Path.Combine(_targetDir, "MO2", "plugins", "ext.dll"))
            .Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Тест 5: Оба непусты → оба шага сработали независимо
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_BothExtensionsAndExtras_AllWritten()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");

        var (mo2Name, mo2Hash, mo2Size) =
            CreateMo2Archive(downloadsDir);
        var (modName, modArchiveHash, modArchiveSize, fileHashes) =
            CreateModArchive(downloadsDir, "TestMod.7z",
                ("file.txt", "hello from mod"),
                ("plugins/ext.dll", "ext dll content"),
                ("extra.exe", "extra exe content"));

        var (modFileHash, modFileSize) = fileHashes["file.txt"];
        var (extFileHash, extFileSize) = fileHashes["plugins/ext.dll"];
        var (extraFileHash, extraFileSize) = fileHashes["extra.exe"];

        var manifest = MakeManifest(
            mo2Name, mo2Hash, mo2Size,
            modName, modArchiveHash, modArchiveSize,
            modFileHash, modFileSize,
            withExtensions: true,
            withExtras: true,
            extFileHash: extFileHash,
            extFileSize: extFileSize,
            extraFileHash: extraFileHash,
            extraFileSize: extraFileSize);
        var manifestPath = WriteManifest(manifest);

        var pipeline = BuildPipeline();
        var output = await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);

        // Оба файла на месте.
        File.Exists(Path.Combine(_targetDir, "MO2", "plugins", "ext.dll"))
            .Should().BeTrue();
        File.Exists(Path.Combine(_targetDir, "Stock Game", "extra.exe"))
            .Should().BeTrue();

        // Оба шага независимо отчитались.
        output.ExecuteExtensions.Written.Should().ContainSingle()
            .Which.Should().Be("fake-ext");
        output.ExecuteExtras.Written.Should().ContainSingle()
            .Which.Should().Be("fake-extra");

        // Мод тоже разложен.
        File.Exists(Path.Combine(
            _targetDir, "MO2", "mods", "TestMod", "file.txt"))
            .Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Тест 6: Архива нет в downloads/, downloader-а нет → ошибка
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ModArchiveMissing_Throws()
    {
        var downloadsDir = Path.Combine(_targetDir, "MO2", "downloads");

        var (mo2Name, mo2Hash, mo2Size) = CreateMo2Archive(downloadsDir);

        var fakeModHash = new XxHash64Value(0x1234567890ABCDEF);

        var manifest = MakeManifest(
            mo2Name, mo2Hash, mo2Size,
            modArchiveName: "MissingMod.7z",
            modArchiveHash: fakeModHash,
            modArchiveSize: 100,
            modFileHash: new XxHash64Value(0xFEDCBA0987654321),
            modFileSize: 100);
        var manifestPath = WriteManifest(manifest);

        var pipeline = BuildPipeline();

        var act = async () => await pipeline.ExecuteAsync(
            MakeInput(manifestPath), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*MissingMod.7z*");
    }
}
