using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Gui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class InstalledPackScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancesRoot;

    public InstalledPackScannerTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-scan-" + Guid.NewGuid());
        _instancesRoot = Path.Combine(_tempDir, "Instances");
        Directory.CreateDirectory(_instancesRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private InstalledPackScanner MakeScanner() =>
        new(_instancesRoot, NullLogger<InstalledPackScanner>.Instance);

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private static ModlistManifest MakeManifest(
        string name = "Test Pack",
        string version = "1.0.0",
        string game = "skyrimspecialedition",
        string gameVersion = "1.6.1170",
        DateTimeOffset? createdAt = null)
    {
        return new ModlistManifest
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = version,
            CreatedAt = createdAt ?? new DateTimeOffset(
                2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = name,
                Version = version,
                Author = "tester",
                Game = game,
                GameVersion = gameVersion,
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
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };
    }

    private string CreateInstance(string folderName, ModlistManifest manifest)
    {
        var instancePath = Path.Combine(_instancesRoot, folderName);
        Directory.CreateDirectory(instancePath);
        ManifestJson.Save(
            Path.Combine(instancePath, "modlist.json"), manifest);
        return instancePath;
    }

    // ------------------------------------------------------------------
    //  Пустые состояния
    // ------------------------------------------------------------------

    [Fact]
    public void Scan_InstancesRootDoesNotExist_ReturnsEmpty()
    {
        Directory.Delete(_instancesRoot, recursive: true);

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_EmptyInstancesRoot_ReturnsEmpty()
    {
        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_FolderWithoutManifest_Skipped()
    {
        Directory.CreateDirectory(Path.Combine(_instancesRoot, "EmptyFolder"));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Один инстанс
    // ------------------------------------------------------------------

    [Fact]
    public void Scan_OneInstance_ReturnsInfo()
    {
        var instancePath = CreateInstance("MyPack", MakeManifest(
            name: "My Pack",
            version: "1.2.3",
            game: "skyrimspecialedition",
            gameVersion: "1.6.1170"));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().HaveCount(1);
        var info = result[0];
        info.Name.Should().Be("My Pack");
        info.Version.Should().Be("1.2.3");
        info.Game.Should().Be("skyrimspecialedition");
        info.GameVersion.Should().Be("1.6.1170");
        info.InstancePath.Should().Be(instancePath);
        info.ManifestPath.Should().Be(
            Path.Combine(instancePath, "modlist.json"));
    }

    [Fact]
    public void Scan_CreatedAt_Propagated()
    {
        var created = new DateTimeOffset(
            2026, 8, 15, 10, 30, 0, TimeSpan.Zero);
        CreateInstance("MyPack", MakeManifest(createdAt: created));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result[0].CreatedAt.Should().Be(created);
    }

    // ------------------------------------------------------------------
    //  Несколько инстансов
    // ------------------------------------------------------------------

    [Fact]
    public void Scan_MultipleInstances_AllReturned()
    {
        CreateInstance("PackA", MakeManifest(name: "Pack A"));
        CreateInstance("PackB", MakeManifest(name: "Pack B"));
        CreateInstance("PackC", MakeManifest(name: "Pack C"));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().HaveCount(3);
        result.Select(i => i.Name).Should().Equal("Pack A", "Pack B", "Pack C");
    }

    [Fact]
    public void Scan_ResultSortedByNameOrdinal()
    {
        CreateInstance("Z", MakeManifest(name: "Zeta"));
        CreateInstance("A", MakeManifest(name: "Alpha"));
        CreateInstance("M", MakeManifest(name: "Mu"));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Select(i => i.Name).Should().Equal("Alpha", "Mu", "Zeta");
    }

    [Fact]
    public void Scan_DisplayNameFromManifest_NotFromFolderName()
    {
        CreateInstance("wrong-folder-name",
            MakeManifest(name: "Correct Name"));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Correct Name");
        result[0].InstancePath.Should().EndWith("wrong-folder-name");
    }

    // ------------------------------------------------------------------
    //  Битые манифесты
    // ------------------------------------------------------------------

    [Fact]
    public void Scan_MalformedManifest_Skipped()
    {
        var instancePath = Path.Combine(_instancesRoot, "Broken");
        Directory.CreateDirectory(instancePath);
        File.WriteAllText(
            Path.Combine(instancePath, "modlist.json"),
            "{ this is not json }");

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_MalformedManifest_DoesNotKillValidOnes()
    {
        CreateInstance("GoodA", MakeManifest(name: "Good A"));

        var brokenPath = Path.Combine(_instancesRoot, "Broken");
        Directory.CreateDirectory(brokenPath);
        File.WriteAllText(
            Path.Combine(brokenPath, "modlist.json"),
            "{ garbage }");

        CreateInstance("GoodB", MakeManifest(name: "Good B"));

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().HaveCount(2);
        result.Select(i => i.Name).Should().Equal("Good A", "Good B");
    }

    [Fact]
    public void Scan_EmptyManifestFile_Skipped()
    {
        var instancePath = Path.Combine(_instancesRoot, "Empty");
        Directory.CreateDirectory(instancePath);
        File.WriteAllText(
            Path.Combine(instancePath, "modlist.json"), "");

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Смешанный сценарий
    // ------------------------------------------------------------------

    [Fact]
    public void Scan_MixedInstances_OnlyValidReturned()
    {
        CreateInstance("ManifestOnly", MakeManifest(name: "Manifest Only"));
        Directory.CreateDirectory(Path.Combine(_instancesRoot, "NoManifest"));

        var brokenPath = Path.Combine(_instancesRoot, "Broken");
        Directory.CreateDirectory(brokenPath);
        File.WriteAllText(
            Path.Combine(brokenPath, "modlist.json"), "{ broken }");

        var scanner = MakeScanner();
        var result = scanner.Scan();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Manifest Only");
    }
}
