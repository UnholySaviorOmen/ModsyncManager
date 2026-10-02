using Modsync.Gui.Shared.Models;
using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.Tests.Fakes;

public sealed class FakeInstalledPackScanner : IInstalledPackScanner
{
    public List<InstalledPackInfo> Packs { get; } = new();

    public int ScanCallCount { get; private set; }

    public IReadOnlyList<InstalledPackInfo> Scan()
    {
        ScanCallCount++;
        return Packs;
    }

    public static InstalledPackInfo MakePack(
        string name = "Test Pack",
        string version = "1.0.0",
        string game = "skyrimspecialedition",
        string gameVersion = "1.6.1170",
        string? instancePath = null,
        string? manifestPath = null)
    {
        var inst = instancePath ?? $"/test/Instances/{name}";
        return new InstalledPackInfo
        {
            Name = name,
            Version = version,
            Game = game,
            GameVersion = gameVersion,
            CreatedAt = new DateTimeOffset(
                2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
            InstancePath = inst,
            ManifestPath = manifestPath
                ?? System.IO.Path.Combine(inst, "modlist.json"),
        };
    }
}
