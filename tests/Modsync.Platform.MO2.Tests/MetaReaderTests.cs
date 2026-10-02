using FluentAssertions;
using Modsync.Platform.MO2.Readers;

namespace Modsync.Platform.MO2.Tests;

public class MetaReaderTests
{
    [Fact]
    public void Parse_ValidNexusMeta_ReturnsMetaFile()
    {
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim",
            "modID=3863",
            "fileID=1000172397",
        };

        var meta = MetaReader.Parse(lines);

        meta.Should().NotBeNull();
        meta!.GameName.Should().Be("Skyrim");
        meta.ModId.Should().Be(3863);
        meta.FileId.Should().Be(1000172397);
    }

    [Fact]
    public void Parse_WithInstalledFilesSection_IgnoresExtraData()
    {
        // Реальный .meta от MO2 содержит секцию [installedFiles] с хешами.
        // Она нам не нужна — парсер должен её игнорировать.
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim",
            "modID=3863",
            "fileID=1000172397",
            "",
            "[installedFiles]",
            "1\\interface\\iconmenu.swf=ABCD1234",
            "2\\scripts\\skyui.pex=EF567890",
        };

        var meta = MetaReader.Parse(lines);

        meta.Should().NotBeNull();
        meta!.ModId.Should().Be(3863);
        meta.FileId.Should().Be(1000172397);
    }

    [Fact]
    public void Parse_WithBom_Succeeds()
    {
        var lines = new[]
        {
            "\uFEFF[General]",
            "gameName=Skyrim",
            "modID=3863",
            "fileID=1000172397",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().NotBeNull();
        meta!.ModId.Should().Be(3863);
    }

    [Fact]
    public void Parse_WithoutGeneralSection_ReturnsNull()
    {
        // .meta от Wabbajack иногда имеет только [General]. Но если секции нет
        // вообще — не наш формат.
        var lines = new[]
        {
            "[SomethingElse]",
            "gameName=Skyrim",
            "modID=3863",
            "fileID=1000172397",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().BeNull();
    }

    [Fact]
    public void Parse_NonNexusFormat_ReturnsNull()
    {
        // Некоторые .meta не от Nexus: directURL, manualURL, IPS4.
        // У них нет modID/fileID. Мы должны вернуть null.
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim",
            "directURL=https://example.com/mod.7z",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingFileId_ReturnsNull()
    {
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim",
            "modID=3863",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().BeNull();
    }

    [Fact]
    public void Parse_NonIntegerModId_ReturnsNull()
    {
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim",
            "modID=not-a-number",
            "fileID=1000172397",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().BeNull();
    }

    [Fact]
    public void Parse_LowercaseKeys_ReturnsNull()
    {
        // Wabbajack пишет modID и fileID с большой ID.
        // Ключи case-sensitive. Если кто-то напишет modid — не наш формат.
        var lines = new[]
        {
            "[General]",
            "gameName=Skyrim",
            "modid=3863",
            "fileid=1000172397",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().BeNull();
    }

    [Fact]
    public void Parse_SectionNameCaseInsensitive()
    {
        // [General], [general], [GENERAL] — все валидны.
        var lines = new[]
        {
            "[general]",
            "gameName=Skyrim",
            "modID=3863",
            "fileID=1000172397",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().NotBeNull();
        meta!.ModId.Should().Be(3863);
    }

    [Fact]
    public void Parse_WithCommentsAndEmptyLines_Succeeds()
    {
        var lines = new[]
        {
            "; comment",
            "",
            "[General]",
            "  ",
            "gameName=Skyrim",
            "# another comment",
            "modID=3863",
            "fileID=1000172397",
        };

        var meta = MetaReader.Parse(lines);
        meta.Should().NotBeNull();
        meta!.ModId.Should().Be(3863);
    }

    [Fact]
    public void TryRead_NonExistentFile_ReturnsNull()
    {
        var meta = MetaReader.TryRead(@"C:\this\path\does\not\exist.meta");
        meta.Should().BeNull();
    }

    [Fact]
    public void TryRead_RealFile_Roundtrip()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tmp, new[]
            {
                "[General]",
                "gameName=Skyrim",
                "modID=3863",
                "fileID=1000172397",
            });

            var meta = MetaReader.TryRead(tmp);

            meta.Should().NotBeNull();
            meta!.GameName.Should().Be("Skyrim");
            meta.ModId.Should().Be(3863);
            meta.FileId.Should().Be(1000172397);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void TryRead_RealWorldSample_ParsesCorrectly()
    {
        // Реальный пример из MO2 (с BOM, с [installedFiles]).
        var content = "\uFEFF[General]\r\n" +
                      "gameName=Skyrim\r\n" +
                      "modID=12604\r\n" +
                      "fileID=1234567890\r\n" +
                      "\r\n" +
                      "[installedFiles]\r\n" +
                      "1\\interface\\iconmenu.swf=DEADBEEF\r\n";

        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, content);

            var meta = MetaReader.TryRead(tmp);

            meta.Should().NotBeNull();
            meta!.ModId.Should().Be(12604);
            meta.FileId.Should().Be(1234567890);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
