using System.Text.Json;
using FluentAssertions;
using Modsync.Core.Models.Pack;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Hashing;

namespace Modsync.Core.Tests;

public class PackConfigJsonTests
{
    private static PackConfig MakeConfig() => new()
    {
        Meta = new PackMeta
        {
            Name = "Test Pack",
            Version = "1.0.0",
            Author = "tester",
            Game = "skyrimspecialedition",
            GameVersion = "1.6.1170",
        },
        Instance = new PackInstance { Path = "Test Pack" },
        Mo2 = new PackMo2
        {
            Version = "2.5.2",
            Profile = "Default",
            Archive = "Mod.Organizer-2.5.2.7z",
            Source = new MirrorSourceRef
            {
                Url = "https://github.com/ModOrganizer2/modorganizer/releases/download/v2.5.2/Mod.Organizer-2.5.2.7z",
                Hash = new XxHash64Value(0x0000000000000001),
            },
            Extensions = Array.Empty<string>(),
        },
        StockGame = new PackStockGame { Extras = Array.Empty<string>() },
        ArchiveSources = Array.Empty<PackArchiveSource>(),
    };

    [Fact]
    public void Roundtrip_MinimalConfig_PreservesData()
    {
        var config = MakeConfig();

        var json = PackConfigJson.Serialize(config);
        var back = PackConfigJson.Deserialize(json);

        back.Meta.Name.Should().Be("Test Pack");
        back.Instance.Path.Should().Be("Test Pack");
        back.Mo2.Profile.Should().Be("Default");
        back.Mo2.Source.Should().BeOfType<MirrorSourceRef>();
        back.ArchiveSources.Should().BeEmpty();
    }

    [Fact]
    public void Roundtrip_FullConfig_PreservesAllSources()
    {
        var config = MakeConfig() with
        {
            Instance = new PackInstance { Path = "NordicUI Overhaul" },
            Mo2 = MakeConfig().Mo2 with
            {
                Profile = "NordicUI",
                Extensions = new[]
                {
                    "plugins/fomod_plus_installer.dll",
                    "tools/BethINI/",
                },
            },
            StockGame = new PackStockGame
            {
                Extras = new[] { "skse64_loader.exe", "enbseries/" },
            },
            ArchiveSources = new[]
            {
                new PackArchiveSource
                {
                    Archive = "SomeMod.7z",
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://cdn.example.com/SomeMod.7z",
                            Hash = new XxHash64Value(0xabc),
                        },
                        new NexusSourceRef
                        {
                            ModId = 12345,
                            FileId = 67890,
                            Game = "skyrimspecialedition",
                        },
                    },
                },
            },
        };

        var json = PackConfigJson.Serialize(config);
        var back = PackConfigJson.Deserialize(json);

        back.Instance.Path.Should().Be("NordicUI Overhaul");
        back.Mo2.Profile.Should().Be("NordicUI");
        back.Mo2.Extensions.Should().HaveCount(2);
        back.StockGame.Extras.Should().HaveCount(2);
        back.ArchiveSources.Should().HaveCount(1);
        back.ArchiveSources[0].Sources.Should().HaveCount(2);
        back.ArchiveSources[0].Sources[0].Should().BeOfType<MirrorSourceRef>();
        back.ArchiveSources[0].Sources[1].Should().BeOfType<NexusSourceRef>();
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var json = PackConfigJson.Serialize(MakeConfig());

        json.Should().Contain("\"meta\"");
        json.Should().Contain("\"instance\"");
        json.Should().Contain("\"gameVersion\"");
        json.Should().Contain("\"profile\"");
        json.Should().Contain("\"archiveSources\"");
    }

    [Fact]
    public void Deserialize_RealWorldJson_ParsesCorrectly()
    {
        var json = """
        {
          "meta": {
            "name": "Nordic UI Overhaul",
            "version": "1.2.0",
            "author": "Username",
            "game": "skyrimspecialedition",
            "gameVersion": "1.6.1170"
          },
          "instance": {
            "path": "NordicUI Overhaul"
          },
          "mo2": {
            "version": "2.5.2",
            "profile": "NordicUI",
            "archive": "Mod.Organizer-2.5.2.7z",
            "source": {
              "type": "mirror",
              "url": "https://github.com/ModOrganizer2/modorganizer/releases/download/v2.5.2/Mod.Organizer-2.5.2.7z",
              "hash": "xxh64:0000000000000001"
            },
            "extensions": []
          },
          "stockGame": {
            "extras": []
          },
          "archiveSources": []
        }
        """;

        var config = PackConfigJson.Deserialize(json);

        config.Meta.Name.Should().Be("Nordic UI Overhaul");
        config.Instance.Path.Should().Be("NordicUI Overhaul");
        config.Mo2.Profile.Should().Be("NordicUI");
        config.Mo2.Source.Should().BeOfType<MirrorSourceRef>();
    }

    [Fact]
    public void Deserialize_EmptyJson_Throws()
    {
        var act = () => PackConfigJson.Deserialize("{}");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task SaveAsync_LoadAsync_Roundtrip()
    {
        var config = MakeConfig();

        var tmp = Path.GetTempFileName();
        try
        {
            await PackConfigJson.SaveAsync(tmp, config, CancellationToken.None);
            var back = await PackConfigJson.LoadAsync(tmp, CancellationToken.None);

            back.Meta.Name.Should().Be("Test Pack");
            back.Instance.Path.Should().Be("Test Pack");
            back.Mo2.Profile.Should().Be("Default");
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
