using FluentAssertions;
using Modsync.Core.Models.Hashing;
using Modsync.Core.Models.Manifest.Sources;
using Modsync.Core.Models.Pack;
using Modsync.Core.Validation;

namespace Modsync.Core.Tests;

public class PackConfigValidatorTests
{
    private static PackConfig MakeValidConfig() => new()
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
                Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                Hash = new XxHash64Value(0xE574E05EB6C470AD),
            },
            Extensions = Array.Empty<string>(),
        },
        StockGame = new PackStockGame { Extras = Array.Empty<string>() },
        ArchiveSources = Array.Empty<PackArchiveSource>(),
    };

    [Fact]
    public void Validate_MinimalValidConfig_Pass()
    {
        PackConfigValidator.Validate(MakeValidConfig()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_FullValidConfig_Pass()
    {
        var config = MakeValidConfig() with
        {
            Instance = new PackInstance { Path = "NordicUI Overhaul" },
            Mo2 = MakeValidConfig().Mo2 with
            {
                Profile = "NordicUI",
                Extensions = new[] { "plugins/fomod.dll", "tools/BethINI/" },
            },
            StockGame = new PackStockGame { Extras = new[] { "skse64_loader.exe" } },
            ArchiveSources = new[]
            {
                new PackArchiveSource
                {
                    Archive = "SomeMod.7z",
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/x.7z",
                            Hash = new XxHash64Value(0xabc),
                        },
                    },
                },
            },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeTrue(
            $"errors: {string.Join("; ", result.Errors)}");
    }

    [Fact]
    public void Validate_InvalidName_Fails()
    {
        var config = MakeValidConfig() with
        {
            Meta = MakeValidConfig().Meta with { Name = "CON" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("reserved"));
    }

    [Fact]
    public void Validate_InvalidVersion_Fails()
    {
        var config = MakeValidConfig() with
        {
            Meta = MakeValidConfig().Meta with { Version = "v1.0" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("semver"));
    }

    [Fact]
    public void Validate_EmptyGame_Fails()
    {
        var config = MakeValidConfig() with
        {
            Meta = MakeValidConfig().Meta with { Game = "" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("meta.game"));
    }

    [Fact]
    public void Validate_InvalidInstancePath_Fails()
    {
        var config = MakeValidConfig() with
        {
            Instance = new PackInstance { Path = "../outside" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("instance.path"));
    }

    [Fact]
    public void Validate_InvalidProfileName_Fails()
    {
        var config = MakeValidConfig() with
        {
            Mo2 = MakeValidConfig().Mo2 with { Profile = "CON" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("mo2.profile"));
    }

    [Fact]
    public void Validate_Mo2SourceNotMirror_Fails()
    {
        var config = MakeValidConfig() with
        {
            Mo2 = MakeValidConfig().Mo2 with
            {
                Source = new NexusSourceRef
                {
                    ModId = 1,
                    FileId = 1,
                    Game = "skyrimspecialedition",
                },
            },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("mo2.source"));
        result.Errors.Should().Contain(e => e.Contains("mirror"));
    }

    [Fact]
    public void Validate_Mo2ArchiveWithPath_Fails()
    {
        var config = MakeValidConfig() with
        {
            Mo2 = MakeValidConfig().Mo2 with { Archive = "subdir/Mod.7z" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("mo2.archive"));
    }

    [Fact]
    public void Validate_AbsoluteExtensionPath_Fails()
    {
        var config = MakeValidConfig() with
        {
            Mo2 = MakeValidConfig().Mo2 with
            {
                Extensions = new[] { "C:\\absolute\\path" },
            },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("mo2.extensions[0]"));
    }

    [Fact]
    public void Validate_DuplicateArchiveSources_Fails()
    {
        var config = MakeValidConfig() with
        {
            ArchiveSources = new[]
            {
                new PackArchiveSource
                {
                    Archive = "SomeMod.7z",
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/x.7z",
                            Hash = new XxHash64Value(0xabc),
                        },
                    },
                },
                new PackArchiveSource
                {
                    Archive = "somemod.7z",
                    Sources = new ArchiveSourceRef[]
                    {
                        new MirrorSourceRef
                        {
                            Url = "https://example.com/y.7z",
                            Hash = new XxHash64Value(0xdef),
                        },
                    },
                },
            },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("duplicate"));
    }

    [Fact]
    public void Validate_EmptySources_Fails()
    {
        var config = MakeValidConfig() with
        {
            ArchiveSources = new[]
            {
                new PackArchiveSource
                {
                    Archive = "SomeMod.7z",
                    Sources = Array.Empty<ArchiveSourceRef>(),
                },
            },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one source"));
    }

    [Fact]
    public void Validate_MultipleErrors_ReportsAll()
    {
        var config = MakeValidConfig() with
        {
            Meta = MakeValidConfig().Meta with { Name = "CON", Version = "bad" },
        };

        var result = PackConfigValidator.Validate(config);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
    }
}
