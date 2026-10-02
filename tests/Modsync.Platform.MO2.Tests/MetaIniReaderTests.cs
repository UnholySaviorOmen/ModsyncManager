using FluentAssertions;
using Modsync.Core.Models.Manifest;
using Modsync.Platform.MO2.Readers;

namespace Modsync.Platform.MO2.Tests;

public class MetaIniReaderTests
{
    [Fact]
    public void Parse_FullGeneralSection_ReadsAllFields()
    {
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim Special Edition",
            "gameID=skyrimspecialedition",
            "modID=32349",
            "fileID=795423",
            "version=1.7.0",
            "repository=Nexus",
            "url=https://www.nexusmods.com/skyrimspecialedition/mods/32349",
            "comments=some comment",
            "notes=my author note",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.GameName.Should().Be("Skyrim Special Edition");
        meta.GameId.Should().Be("skyrimspecialedition");
        meta.ModId.Should().Be(32349);
        meta.FileId.Should().Be(795423);
        meta.Version.Should().Be("1.7.0");
        meta.Repository.Should().Be("Nexus");
        meta.Url.Should().Be("https://www.nexusmods.com/skyrimspecialedition/mods/32349");
        meta.Comments.Should().Be("some comment");
        meta.Notes.Should().Be("my author note");
        meta.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Parse_LowercaseKeys_ReadsCorrectly()
    {
        // MO2 в некоторых версиях пишет modid/fileid/gameid/gamename lowercase.
        var lines = new[]
        {
            "[General]",
            "gamename=SkyrimSE",
            "gameid=skyrimspecialedition",
            "modid=18967",
            "fileid=796897",
            "version=1.9.4.0",
            "repository=Nexus",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.GameName.Should().Be("SkyrimSE");
        meta.GameId.Should().Be("skyrimspecialedition");
        meta.ModId.Should().Be(18967);
        meta.FileId.Should().Be(796897);
        meta.Version.Should().Be("1.9.4.0");
        meta.Repository.Should().Be("Nexus");
    }

    [Fact]
    public void Parse_MixedCaseKeys_ReadsCorrectly()
    {
        var lines = new[]
        {
            "[General]",
            "GameName=SkyrimSE",
            "GameID=skyrimspecialedition",
            "modid=1",
            "FILEID=2",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.GameName.Should().Be("SkyrimSE");
        meta.GameId.Should().Be("skyrimspecialedition");
        meta.ModId.Should().Be(1);
        meta.FileId.Should().Be(2);
    }

    [Fact]
    public void Parse_IgnoresCategory()
    {
        // MO2 пишет category как "7," или "7,15," — парсер игнорирует.
        var lines = new[]
        {
            "[General]",
            "modID=1",
            "category=\"7,15,\"",
        };

        var meta = MetaIniReader.Parse(lines);
        meta.ModId.Should().Be(1);
    }

    [Fact]
    public void Parse_IgnoresUnknownFields()
    {
        // MO2 пишет много полей, которые мы не храним.
        // Они не должны ломать парсер.
        var lines = new[]
        {
            "[General]",
            "gameName=SkyrimSE",
            "modid=18967",
            "newestVersion=1.9.4.0",
            "nexusFileStatus=1",
            "installationFile=Better Jumping NG 18967 1.9.4.zip",
            "nexusDescription=\"long description\"",
            "hasCustomURL=false",
            "lastNexusQuery=2026-09-14T21:34:09Z",
            "converted=false",
            "validated=false",
            "color=@Variant(...)",
            "endorsed=1",
            "tracked=0",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.GameName.Should().Be("SkyrimSE");
        meta.ModId.Should().Be(18967);
    }

    [Fact]
    public void Parse_PartialGeneralSection_LeavesOthersNull()
    {
        var lines = new[]
        {
            "[General]",
            "modID=1",
            "version=2.0",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.ModId.Should().Be(1);
        meta.Version.Should().Be("2.0");
        meta.FileId.Should().BeNull();
        meta.GameName.Should().BeNull();
        meta.Repository.Should().BeNull();
        meta.Url.Should().BeNull();
        meta.Comments.Should().BeNull();
        meta.Notes.Should().BeNull();
    }

    [Fact]
    public void Parse_IgnoresInstalledFilesSection()
    {
        var lines = new[]
        {
            "[General]",
            "modID=1",
            "fileID=2",
            "version=1.0",
            "",
            "[installedFiles]",
            "1\\SKSE\\Plugins\\Foo.dll=DEADBEEF",
            "2\\SKSE\\Plugins\\Foo.pdb=FEEDFACE",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.ModId.Should().Be(1);
        meta.FileId.Should().Be(2);
        meta.Version.Should().Be("1.0");
    }

    [Fact]
    public void Parse_WithoutGeneralSection_ReturnsEmpty()
    {
        var lines = new[]
        {
            "[installedFiles]",
            "1\\foo.dll=DEADBEEF",
        };

        var meta = MetaIniReader.Parse(lines);

        meta.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        var meta = MetaIniReader.Parse(Array.Empty<string>());
        meta.IsEmpty.Should().BeTrue();
        meta.Should().BeEquivalentTo(ModMeta.Empty);
    }

    [Fact]
    public void Parse_WithBom_Succeeds()
    {
        var lines = new[]
        {
            "\uFEFF[General]",
            "modID=1",
            "fileID=2",
        };

        var meta = MetaIniReader.Parse(lines);
        meta.ModId.Should().Be(1);
        meta.FileId.Should().Be(2);
    }

    [Fact]
    public void Parse_SectionNameCaseInsensitive()
    {
        var lines = new[]
        {
            "[GENERAL]",
            "modID=1",
        };

        var meta = MetaIniReader.Parse(lines);
        meta.ModId.Should().Be(1);
    }

    [Fact]
    public void Parse_NonNumericModId_LeavesNull()
    {
        var lines = new[]
        {
            "[General]",
            "modID=not-a-number",
            "fileID=2",
        };

        var meta = MetaIniReader.Parse(lines);
        meta.ModId.Should().BeNull();
        meta.FileId.Should().Be(2);
    }

    [Fact]
    public void Parse_WithCommentsAndEmptyLines_IgnoresThem()
    {
        var lines = new[]
        {
            "; comment",
            "# another",
            "",
            "[General]",
            "   ",
            "modID=1",
            "; another comment",
            "fileID=2",
        };

        var meta = MetaIniReader.Parse(lines);
        meta.ModId.Should().Be(1);
        meta.FileId.Should().Be(2);
    }

    [Fact]
    public void Parse_DuplicateKeys_FirstWins()
    {
        var lines = new[]
        {
            "[General]",
            "modID=1",
            "modID=2",
        };

        var meta = MetaIniReader.Parse(lines);
        meta.ModId.Should().Be(1);
    }

    [Fact]
    public void TryRead_NonExistentFile_ReturnsNull()
    {
        var meta = MetaIniReader.TryRead(@"C:\does\not\exist\meta.ini");
        meta.Should().BeNull();
    }

    [Fact]
    public void TryRead_RealFile_ParsesCorrectly()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "\uFEFF[General]\r\n" +
                "gameName=Skyrim Special Edition\r\n" +
                "gameID=skyrimspecialedition\r\n" +
                "modID=32349\r\n" +
                "fileID=795423\r\n" +
                "version=1.7.0\r\n" +
                "\r\n" +
                "[installedFiles]\r\n" +
                "1\\SKSE\\Plugins\\ActorLimitFix.dll=DEADBEEF\r\n");

            var meta = MetaIniReader.TryRead(tmp);

            meta.Should().NotBeNull();
            meta!.ModId.Should().Be(32349);
            meta.FileId.Should().Be(795423);
            meta.Version.Should().Be("1.7.0");
            meta.GameId.Should().Be("skyrimspecialedition");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void TryRead_RealWorldLowercaseFile_ParsesCorrectly()
    {
        // Реальный формат из OmenRim 7: lowercase ключи.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp,
                "[General]\r\n" +
                "gameName=SkyrimSE\r\n" +
                "modid=18967\r\n" +
                "version=1.9.4.0\r\n" +
                "newestVersion=1.9.4.0\r\n" +
                "category=\"7,\"\r\n" +
                "nexusFileStatus=1\r\n" +
                "installationFile=Better Jumping NG 18967 1.9.4 2026-08-29T08-15Z Ae46W7G6Z.zip\r\n" +
                "repository=Nexus\r\n" +
                "ignoredVersion=\r\n" +
                "comments=\r\n" +
                "notes=\r\n" +
                "url=\r\n" +
                "hasCustomURL=false\r\n" +
                "endorsed=1\r\n" +
                "tracked=0\r\n" +
                "\r\n" +
                "[installedFiles]\r\n" +
                "1\\modid=18967\r\n" +
                "size=1\r\n" +
                "1\\fileid=796897\r\n");

            var meta = MetaIniReader.TryRead(tmp);

            meta.Should().NotBeNull();
            meta!.GameName.Should().Be("SkyrimSE");
            meta.ModId.Should().Be(18967);
            meta.Version.Should().Be("1.9.4.0");
            meta.Repository.Should().Be("Nexus");
            meta.Comments.Should().Be("");
            meta.Notes.Should().Be("");
            meta.Url.Should().Be("");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Empty_IsEmpty_True()
    {
        ModMeta.Empty.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_WithOnlyGameName_False()
    {
        var m = new ModMeta { GameName = "Skyrim" };
        m.IsEmpty.Should().BeFalse();
    }
}
