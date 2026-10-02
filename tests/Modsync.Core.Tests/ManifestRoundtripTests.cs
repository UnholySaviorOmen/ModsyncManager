using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest;
using Modsync.Core.Models.Manifest.Directives;
using Modsync.Core.Models.Manifest.Sources;

namespace Modsync.Core.Tests;

public class ManifestRoundtripTests
{
    [Fact]
    public void Roundtrip_MinimalManifest_PreservesData()
    {
        var manifest = new ModlistManifest
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = DateTimeOffset.Parse("2026-09-13T10:00:00Z"),
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "me",
                Game = "SkyrimSE",
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
                    Size = 1,
                    Hash = new XxHash64Value(0xabc),
                    Sources = Array.Empty<ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection { Extras = Array.Empty<ExtensionEntry>() },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = new[]
            {
                new ModEntry
                {
                    Name = "SkyUI", Enabled = true, Order = 0,
                    Directives = new Directive[]
                    {
                        new CreateDirectoryDirective { Destination = "meshes/" }
                    }
                }
            },
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

        var json = ManifestJson.Serialize(manifest);
        var back = ManifestJson.Deserialize(json);

        back.Meta.Name.Should().Be("Test");
        back.Mods.Should().HaveCount(1);
        back.Mods[0].Directives[0].Should().BeOfType<CreateDirectoryDirective>();
    }

    [Fact]
    public void Serialize_CreatedAtEndsWithZ()
    {
        var manifest = new ModlistManifest
        {
            SchemaVersion = "1.0.0",
            ManifestVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 9, 16, 21, 30, 17, 310, TimeSpan.Zero),
            CreatedBy = "modsyncmanager-pack/0.1.0",
            Meta = new ManifestMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "me",
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
                    Size = 1,
                    Hash = new XxHash64Value(0xabc),
                    Sources = Array.Empty<ArchiveSourceRef>(),
                },
                Extensions = Array.Empty<ExtensionEntry>(),
            },
            StockGame = new StockGameSection { Extras = Array.Empty<ExtensionEntry>() },
            Archives = Array.Empty<ArchiveEntry>(),
            Mods = Array.Empty<ModEntry>(),
            Plugins = Array.Empty<PluginEntry>(),
            Loadorder = Array.Empty<string>(),
        };

        var json = ManifestJson.Serialize(manifest);

        json.Should().Contain("\"createdAt\": \"2026-09-16T21:30:17.310Z\"");
        json.Should().NotContain("+00:00");
    }

    [Fact]
    public void Hash_FormatsWithPrefix()
    {
        var h = new XxHash64Value(0x1234abcd);
        h.ToString().Should().Be("xxh64:000000001234abcd");

        var parsed = XxHash64Value.Parse(h.ToString());
        parsed.Should().Be(h);
    }
}
