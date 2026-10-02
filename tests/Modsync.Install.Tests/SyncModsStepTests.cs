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
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class SyncModsStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _downloadsDir;
    private readonly string _modsDir;
    private readonly FileHashCache _hashCache = new();
    private readonly SyncModsStep _step;

    public SyncModsStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-sm-" + Guid.NewGuid());
        _downloadsDir = Path.Combine(_tempDir, "downloads");
        _modsDir = Path.Combine(_tempDir, "mods");
        Directory.CreateDirectory(_downloadsDir);
        Directory.CreateDirectory(_modsDir);

        var extractor = new SevenZipExtractor(
            NullLogger<SevenZipExtractor>.Instance);

        _step = new SyncModsStep(
            extractor, _hashCache, NullLogger<SyncModsStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private (ArchiveEntry entry, Dictionary<string, byte[]> content)
        CreateArchive(string fileName, params (string path, string content)[] files)
    {
        var bytesByPath = files.ToDictionary(
            f => f.path,
            f => Encoding.UTF8.GetBytes(f.content),
            StringComparer.Ordinal);

        var archivePath = Path.Combine(_downloadsDir, fileName);
        using (var fs = File.Create(archivePath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (path, content) in files)
            {
                var zipEntry = zip.CreateEntry(path);
                using var es = zipEntry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                es.Write(bytes, 0, bytes.Length);
            }
        }

        var hash = _hashCache.GetOrCompute(archivePath);

        var entry = new ArchiveEntry
        {
            Id = $"local_{Path.GetFileNameWithoutExtension(fileName)}",
            Name = fileName,
            Size = new FileInfo(archivePath).Length,
            Hash = hash,
            Sources = new ArchiveSourceRef[]
            {
                new MirrorSourceRef
                {
                    Url = $"https://example.com/{fileName}",
                    Hash = hash,
                },
            },
        };

        return (entry, bytesByPath);
    }

    private FromArchiveDirective MakeDirective(
        ArchiveEntry archive,
        string sourcePath,
        string destPath,
        byte[] content)
    {
        return new FromArchiveDirective
        {
            Archive = archive.Id,
            Source = sourcePath,
            Destination = destPath,
            Hash = FakeArchiveDownloader.HashOf(content),
            Size = content.Length,
        };
    }

    private static ModEntry MakeMod(
        string name,
        bool enabled = true,
        int order = 0,
        Directive[]? directives = null)
        => new()
        {
            Name = name,
            Enabled = enabled,
            Order = order,
            Directives = directives ?? Array.Empty<Directive>(),
        };

    private static ModlistManifest MakeManifest(
        IReadOnlyList<ModEntry>? mods = null,
        IReadOnlyList<ArchiveEntry>? archives = null)
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
            StockGame = new StockGameSection
            {
                Extras = Array.Empty<ExtensionEntry>(),
            },
            Archives = archives ?? Array.Empty<ArchiveEntry>(),
            Mods = mods ?? Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };
    }

    private SyncModsStep.Input MakeInput(ModlistManifest manifest)
    {
        var archivesById = manifest.Archives
            .ToDictionary(a => a.Id, a => a, StringComparer.Ordinal);

        return new SyncModsStep.Input
        {
            Manifest = manifest,
            DownloadsPath = _downloadsDir,
            ModsPath = _modsDir,
            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
            ArchivesById = archivesById,
        };
    }

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyMods_CreatesAllMods()
    {
        var (archive, _) = CreateArchive("a.zip",
            ("file1.txt", "content1"),
            ("file2.txt", "content2"));

        var mod1 = MakeMod("Mod1", directives: new Directive[]
        {
            MakeDirective(archive, "file1.txt", "file1.txt",
                Encoding.UTF8.GetBytes("content1")),
        });

        var mod2 = MakeMod("Mod2", directives: new Directive[]
        {
            MakeDirective(archive, "file2.txt", "subdir/file2.txt",
                Encoding.UTF8.GetBytes("content2")),
        });

        var manifest = MakeManifest(
            mods: new[] { mod1, mod2 },
            archives: new[] { archive });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Created.Should().BeEquivalentTo(new[] { "Mod1", "Mod2" });
        output.Recreated.Should().BeEmpty();
        output.Skipped.Should().BeEmpty();
        output.Deleted.Should().BeEmpty();

        File.ReadAllText(Path.Combine(_modsDir, "Mod1", "file1.txt"))
            .Should().Be("content1");
        File.ReadAllText(Path.Combine(_modsDir, "Mod2", "subdir", "file2.txt"))
            .Should().Be("content2");
    }

    // ------------------------------------------------------------------
    //  Skip — всё совпадает
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ModUpToDate_IsSkipped()
    {
        var (archive, _) = CreateArchive("b.zip", ("file.txt", "content"));
        var content = Encoding.UTF8.GetBytes("content");

        var mod = MakeMod("Mod", directives: new Directive[]
        {
            MakeDirective(archive, "file.txt", "file.txt", content),
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { archive });

        var input = MakeInput(manifest);
        var first = await _step.ExecuteAsync(input, CancellationToken.None);
        first.Created.Should().ContainSingle();

        var second = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        second.Created.Should().BeEmpty();
        second.Recreated.Should().BeEmpty();
        second.Skipped.Should().ContainSingle().Which.Should().Be("Mod");
    }

    // ------------------------------------------------------------------
    //  Recreate — файл изменён
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_FileChangedInMod_IsRecreated()
    {
        var (archive, _) = CreateArchive("c.zip", ("file.txt", "content"));
        var content = Encoding.UTF8.GetBytes("content");

        var mod = MakeMod("Mod", directives: new Directive[]
        {
            MakeDirective(archive, "file.txt", "file.txt", content),
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { archive });

        var modDir = Path.Combine(_modsDir, "Mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "file.txt"), "WRONG CONTENT");

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Recreated.Should().ContainSingle().Which.Should().Be("Mod");
        File.ReadAllText(Path.Combine(modDir, "file.txt")).Should().Be("content");
    }

    [Fact]
    public async Task Execute_MissingFileInExistingMod_IsRecreated()
    {
        var (archive, _) = CreateArchive("d.zip",
            ("file1.txt", "a"),
            ("file2.txt", "b"));

        var mod = MakeMod("Mod", directives: new Directive[]
        {
            MakeDirective(archive, "file1.txt", "file1.txt",
                Encoding.UTF8.GetBytes("a")),
            MakeDirective(archive, "file2.txt", "file2.txt",
                Encoding.UTF8.GetBytes("b")),
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { archive });

        var modDir = Path.Combine(_modsDir, "Mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "file1.txt"), "a");

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Recreated.Should().ContainSingle().Which.Should().Be("Mod");
        File.Exists(Path.Combine(modDir, "file2.txt")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Удаление лишних папок
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_UndeclaredModFolder_IsDeleted()
    {
        var (archive, _) = CreateArchive("e.zip", ("file.txt", "x"));
        var mod = MakeMod("Mod", directives: new Directive[]
        {
            MakeDirective(archive, "file.txt", "file.txt",
                Encoding.UTF8.GetBytes("x")),
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { archive });

        Directory.CreateDirectory(Path.Combine(_modsDir, "Undeclared"));

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Deleted.Should().ContainSingle().Which.Should().Be("Undeclared");
        Directory.Exists(Path.Combine(_modsDir, "Undeclared")).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  [NoDelete]
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_NoDeleteModFromManifest_IsSkipped()
    {
        var (archive, _) = CreateArchive("f.zip", ("file.txt", "x"));

        var noDeleteMod = MakeMod("[NoDelete]Protected", directives: new Directive[]
        {
            MakeDirective(archive, "file.txt", "file.txt",
                Encoding.UTF8.GetBytes("x")),
        });

        var manifest = MakeManifest(
            mods: new[] { noDeleteMod },
            archives: new[] { archive });

        var modDir = Path.Combine(_modsDir, "[NoDelete]Protected");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "user-file.txt"), "user data");

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Skipped.Should().ContainSingle().Which.Should().Be("[NoDelete]Protected");
        File.Exists(Path.Combine(modDir, "user-file.txt")).Should().BeTrue();
        File.Exists(Path.Combine(modDir, "file.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_UndeclaredNoDeleteFolder_IsNotDeleted()
    {
        var manifest = MakeManifest(
            mods: Array.Empty<ModEntry>(),
            archives: Array.Empty<ArchiveEntry>());

        var userDir = Path.Combine(_modsDir, "[NoDelete]UserMod");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "marker.txt"), "keep");

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Deleted.Should().BeEmpty();
        File.Exists(Path.Combine(userDir, "marker.txt")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Сепараторы (#...)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_SeparatorInManifest_IsSkippedAndNoFolderCreated()
    {
        var separator = MakeMod(
            "# \U0001F4C2 Мои моды_separator",
            enabled: false);

        var manifest = MakeManifest(
            mods: new[] { separator },
            archives: Array.Empty<ArchiveEntry>());

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Skipped.Should().ContainSingle();
        output.Created.Should().BeEmpty();

        // Папки для сепаратора нет.
        Directory.Exists(Path.Combine(_modsDir, "# \U0001F4C2 Мои моды_separator"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Execute_UndeclaredSeparatorFolder_IsNotDeleted()
    {
        var manifest = MakeManifest(
            mods: Array.Empty<ModEntry>(),
            archives: Array.Empty<ArchiveEntry>());

        // Гипотетическая папка с именем, начинающимся на '#'.
        var sepDir = Path.Combine(_modsDir, "#weird");
        Directory.CreateDirectory(sepDir);
        File.WriteAllText(Path.Combine(sepDir, "marker.txt"), "keep");

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Deleted.Should().BeEmpty();
        File.Exists(Path.Combine(sepDir, "marker.txt")).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Empty manifest
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_EmptyManifest_DoesNotThrow()
    {
        var manifest = MakeManifest(
            mods: Array.Empty<ModEntry>(),
            archives: Array.Empty<ArchiveEntry>());

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Created.Should().BeEmpty();
        output.Recreated.Should().BeEmpty();
        output.Skipped.Should().BeEmpty();
        output.Deleted.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Errors
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_MissingArchiveFile_Throws()
    {
        var fakeArchive = new ArchiveEntry
        {
            Id = "local_missing",
            Name = "missing.zip",
            Size = 100,
            Hash = new XxHash64Value(0xabc),
            Sources = Array.Empty<ArchiveSourceRef>(),
        };

        var mod = MakeMod("Mod", directives: new Directive[]
        {
            new FromArchiveDirective
            {
                Archive = fakeArchive.Id,
                Source = "file.txt",
                Destination = "file.txt",
                Hash = new XxHash64Value(0xdef),
                Size = 100,
            },
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { fakeArchive });

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Execute_ArchiveIdNotInManifest_Throws()
    {
        var mod = MakeMod("Mod", directives: new Directive[]
        {
            new FromArchiveDirective
            {
                Archive = "nonexistent-id",
                Source = "file.txt",
                Destination = "file.txt",
                Hash = new XxHash64Value(0xdef),
                Size = 100,
            },
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: Array.Empty<ArchiveEntry>());

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonexistent-id*");
    }

    [Fact]
    public async Task Execute_SourceNotInArchive_Throws()
    {
        var (archive, _) = CreateArchive("g.zip", ("file.txt", "x"));

        var mod = MakeMod("Mod", directives: new Directive[]
        {
            new FromArchiveDirective
            {
                Archive = archive.Id,
                Source = "not-in-archive.txt",
                Destination = "not-in-archive.txt",
                Hash = new XxHash64Value(0xdef),
                Size = 100,
            },
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { archive });

        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not-in-archive.txt*");
    }

    // ------------------------------------------------------------------
    //  Multiple mods, same archive
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_TwoModsSameArchive_BothExtracted()
    {
        var (archive, _) = CreateArchive("shared.zip",
            ("shared1.txt", "one"),
            ("shared2.txt", "two"));

        var mod1 = MakeMod("ModA", directives: new Directive[]
        {
            MakeDirective(archive, "shared1.txt", "file.txt",
                Encoding.UTF8.GetBytes("one")),
        });

        var mod2 = MakeMod("ModB", directives: new Directive[]
        {
            MakeDirective(archive, "shared2.txt", "file.txt",
                Encoding.UTF8.GetBytes("two")),
        });

        var manifest = MakeManifest(
            mods: new[] { mod1, mod2 },
            archives: new[] { archive });

        var output = await _step.ExecuteAsync(MakeInput(manifest), CancellationToken.None);

        output.Created.Should().BeEquivalentTo(new[] { "ModA", "ModB" });

        File.ReadAllText(Path.Combine(_modsDir, "ModA", "file.txt")).Should().Be("one");
        File.ReadAllText(Path.Combine(_modsDir, "ModB", "file.txt")).Should().Be("two");
    }

    // ------------------------------------------------------------------
    //  Cancel
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var manifest = MakeManifest(
            mods: Array.Empty<ModEntry>(),
            archives: Array.Empty<ArchiveEntry>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(MakeInput(manifest), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------
    //  DetailProgress
    // ------------------------------------------------------------------

    /// <summary>
    /// Синхронный IProgress, копит репорты в список.
    ///
    /// DetailProgress в SyncModsStep вызывается из параллельных
    /// воркеров Parallel.ForEachAsync. В текущих тестах стоит
    /// MaxDegreeOfParallelism = 1, но ListProgress должен быть
    /// потокобезопасным — на случай, если тесты поменяются.
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

    [Fact]
    public async Task Execute_DetailProgress_ReportsStartAndFinish()
    {
        var (archive, _) = CreateArchive("dp.zip", ("file.txt", "x"));

        var mods = new[]
        {
            MakeMod("ModA", directives: new Directive[]
            {
                MakeDirective(archive, "file.txt", "file.txt",
                    Encoding.UTF8.GetBytes("x")),
            }),
            MakeMod("ModB", order: 1),
            MakeMod("ModC", order: 2),
        };

        var manifest = MakeManifest(
            mods: mods,
            archives: new[] { archive });

        var progress = new ListProgress();
        var input = MakeInputWithDetail(manifest, progress);

        await _step.ExecuteAsync(input, CancellationToken.None);

        // Стартовый (0, 3) и финальный (3, 3).
        progress.Reports.Should().Contain((0, 3));
        progress.Reports.Should().Contain((3, 3));

        // Финальный репорт — последний.
        progress.Reports.Last().Should().Be((3, 3));
    }

    [Fact]
    public async Task Execute_DetailProgress_ReportsIncrementingCounter()
    {
        var (archive, _) = CreateArchive("dp2.zip", ("file.txt", "x"));

        var mods = Enumerable.Range(0, 5)
            .Select(i => MakeMod($"Mod{i}", order: i))
            .ToArray();

        var manifest = MakeManifest(
            mods: mods,
            archives: new[] { archive });

        var progress = new ListProgress();
        var input = MakeInputWithDetail(manifest, progress);

        await _step.ExecuteAsync(input, CancellationToken.None);

        // Все значения Completed от 0 до 5 должны встретиться.
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
        var (archive, _) = CreateArchive("dp3.zip", ("file.txt", "x"));

        var mod = MakeMod("Mod", directives: new Directive[]
        {
            MakeDirective(archive, "file.txt", "file.txt",
                Encoding.UTF8.GetBytes("x")),
        });

        var manifest = MakeManifest(
            mods: new[] { mod },
            archives: new[] { archive });

        // DetailProgress = null.
        var act = async () => await _step.ExecuteAsync(
            MakeInput(manifest), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private SyncModsStep.Input MakeInputWithDetail(
        ModlistManifest manifest,
        IProgress<(int Completed, int Total)> detailProgress)
    {
        var archivesById = manifest.Archives
            .ToDictionary(a => a.Id, a => a, StringComparer.Ordinal);

        return new SyncModsStep.Input
        {
            Manifest = manifest,
            DownloadsPath = _downloadsDir,
            ModsPath = _modsDir,
            ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 },
            ArchivesById = archivesById,
            DetailProgress = detailProgress,
        };
    }
}
