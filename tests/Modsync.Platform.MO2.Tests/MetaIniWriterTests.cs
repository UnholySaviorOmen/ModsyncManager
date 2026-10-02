using FluentAssertions;
using Modsync.Core.Models.Manifest;
using Modsync.Platform.MO2.Readers;
using Modsync.Platform.MO2.Writers;

namespace Modsync.Platform.MO2.Tests;

public class MetaIniWriterTests : IDisposable
{
    private readonly string _tempDir;

    public MetaIniWriterTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-meta-ini-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Serialize
    // ------------------------------------------------------------------

    [Fact]
    public void Serialize_FullMeta_AllFieldsPresent()
    {
        var meta = new ModMeta
        {
            GameName = "Skyrim Special Edition",
            GameId = "skyrimspecialedition",
            ModId = 32349,
            FileId = 795423,
            Version = "1.7.0",
            Repository = "Nexus",
            Url = "https://www.nexusmods.com/skyrimspecialedition/mods/32349",
            Comments = "some comment",
            Notes = "my author note",
        };

        var text = MetaIniWriter.Serialize(meta);

        text.Should().StartWith("[General]\r\n");
        text.Should().Contain("gameName=Skyrim Special Edition\r\n");
        text.Should().Contain("gameID=skyrimspecialedition\r\n");
        text.Should().Contain("modID=32349\r\n");
        text.Should().Contain("fileID=795423\r\n");
        text.Should().Contain("version=1.7.0\r\n");
        text.Should().Contain("repository=Nexus\r\n");
        text.Should().Contain(
            "url=https://www.nexusmods.com/skyrimspecialedition/mods/32349\r\n");
        text.Should().Contain("comments=some comment\r\n");
        text.Should().Contain("notes=my author note\r\n");
        text.Should().NotContain("[installedFiles]");
        text.Should().NotContain("newestVersion");
        text.Should().NotContain("category");
    }

    [Fact]
    public void Serialize_PartialMeta_OnlyNonNullFields()
    {
        var meta = new ModMeta
        {
            ModId = 1,
            Version = "2.0",
        };

        var text = MetaIniWriter.Serialize(meta);

        text.Should().Contain("modID=1\r\n");
        text.Should().Contain("version=2.0\r\n");
        text.Should().NotContain("gameName=");
        text.Should().NotContain("gameID=");
        text.Should().NotContain("fileID=");
        text.Should().NotContain("repository=");
        text.Should().NotContain("url=");
        text.Should().NotContain("comments=");
        text.Should().NotContain("notes=");
    }

    [Fact]
    public void Serialize_EmptyMeta_OnlyGeneralHeader()
    {
        var text = MetaIniWriter.Serialize(ModMeta.Empty);

        text.Should().Be("[General]\r\n");
    }

    [Fact]
    public void Serialize_EmptyStringValue_Preserved()
    {
        var meta = new ModMeta
        {
            ModId = 1,
            Comments = "",
            Notes = "",
        };

        var text = MetaIniWriter.Serialize(meta);

        text.Should().Contain("comments=\r\n");
        text.Should().Contain("notes=\r\n");
    }

    [Fact]
    public void Serialize_FieldOrder_AsMo2()
    {
        var meta = new ModMeta
        {
            Notes = "n",
            Comments = "c",
            Url = "u",
            Repository = "r",
            Version = "v",
            FileId = 2,
            ModId = 3,
            GameId = "gid",
            GameName = "gn",
        };

        var text = MetaIniWriter.Serialize(meta);
        var lines = text.Split("\r\n", StringSplitOptions.None);

        // lines[0] = [General]
        // lines[1..9] = поля в порядке MO2
        lines[1].Should().Be("gameName=gn");
        lines[2].Should().Be("gameID=gid");
        lines[3].Should().Be("modID=3");
        lines[4].Should().Be("fileID=2");
        lines[5].Should().Be("version=v");
        lines[6].Should().Be("repository=r");
        lines[7].Should().Be("url=u");
        lines[8].Should().Be("comments=c");
        lines[9].Should().Be("notes=n");
        // lines[10] = "" (от финального \r\n)
    }

    // ------------------------------------------------------------------
    //  WriteFile
    // ------------------------------------------------------------------

    [Fact]
    public void WriteFile_CreatesFile()
    {
        var meta = new ModMeta { ModId = 1, Version = "1.0" };
        var path = Path.Combine(_tempDir, "meta.ini");

        MetaIniWriter.WriteFile(path, meta);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void WriteFile_NoBom()
    {
        var meta = new ModMeta { ModId = 1 };
        var path = Path.Combine(_tempDir, "meta.ini");

        MetaIniWriter.WriteFile(path, meta);

        var bytes = File.ReadAllBytes(path);
        var startsWithBom =
            bytes.Length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        startsWithBom.Should().BeFalse();
    }

    [Fact]
    public void WriteFile_CrlfLineEndings()
    {
        var meta = new ModMeta { ModId = 1 };
        var path = Path.Combine(_tempDir, "meta.ini");

        MetaIniWriter.WriteFile(path, meta);

        var text = File.ReadAllText(path);
        text.Should().Contain("\r\n");
    }

    [Fact]
    public void WriteFile_OverwritesExisting()
    {
        var path = Path.Combine(_tempDir, "meta.ini");
        File.WriteAllText(path, "old content");

        MetaIniWriter.WriteFile(path, new ModMeta { ModId = 1 });

        var text = File.ReadAllText(path);
        text.Should().Contain("[General]");
        text.Should().NotContain("old content");
    }

    [Fact]
    public void WriteFile_CreatesParentDirectory()
    {
        var path = Path.Combine(_tempDir, "subdir", "meta.ini");
        MetaIniWriter.WriteFile(path, new ModMeta { ModId = 1 });

        File.Exists(path).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Roundtrip: Writer → Reader
    // ------------------------------------------------------------------

    [Fact]
    public void Roundtrip_FullMeta_Equivalent()
    {
        var original = new ModMeta
        {
            GameName = "Skyrim Special Edition",
            GameId = "skyrimspecialedition",
            ModId = 32349,
            FileId = 795423,
            Version = "1.7.0",
            Repository = "Nexus",
            Url = "https://www.nexusmods.com/skyrimspecialedition/mods/32349",
            Comments = "",
            Notes = "note",
        };

        var path = Path.Combine(_tempDir, "meta.ini");
        MetaIniWriter.WriteFile(path, original);

        var read = MetaIniReader.TryRead(path);

        read.Should().NotBeNull();
        read!.GameName.Should().Be(original.GameName);
        read.GameId.Should().Be(original.GameId);
        read.ModId.Should().Be(original.ModId);
        read.FileId.Should().Be(original.FileId);
        read.Version.Should().Be(original.Version);
        read.Repository.Should().Be(original.Repository);
        read.Url.Should().Be(original.Url);
        read.Comments.Should().Be(original.Comments);
        read.Notes.Should().Be(original.Notes);
    }

    [Fact]
    public void Roundtrip_PartialMeta_Equivalent()
    {
        var original = new ModMeta
        {
            ModId = 1,
            Version = "2.0",
        };

        var path = Path.Combine(_tempDir, "meta.ini");
        MetaIniWriter.WriteFile(path, original);

        var read = MetaIniReader.TryRead(path);

        read.Should().NotBeNull();
        read!.ModId.Should().Be(1);
        read.Version.Should().Be("2.0");
        read.GameName.Should().BeNull();
        read.FileId.Should().BeNull();
    }

    [Fact]
    public void Roundtrip_EmptyMeta_ProducesEmptyModMeta()
    {
        var path = Path.Combine(_tempDir, "meta.ini");
        MetaIniWriter.WriteFile(path, ModMeta.Empty);

        var read = MetaIniReader.TryRead(path);

        read.Should().NotBeNull();
        read!.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Roundtrip_AfterReadFromLowercaseFile_WritesCamelCase()
    {
        // Читаем файл с lowercase-ключами (как в OmenRim 7),
        // потом пишем — должны получить camelCase.
        var lines = new[]
        {
            "[General]",
            "gamename=SkyrimSE",
            "modid=18967",
            "fileid=796897",
            "version=1.9.4.0",
        };

        var meta = MetaIniReader.Parse(lines);

        var text = MetaIniWriter.Serialize(meta);

        text.Should().Contain("gameName=SkyrimSE\r\n");
        text.Should().Contain("modID=18967\r\n");
        text.Should().Contain("fileID=796897\r\n");
        text.Should().Contain("version=1.9.4.0\r\n");
    }
}
