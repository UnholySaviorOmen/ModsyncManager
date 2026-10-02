using FluentAssertions;
using Modsync.Platform.Nexus.Protocol;

namespace Modsync.Platform.Nexus.Tests.Protocol;

public class NxmHandlerBackupTests : IDisposable
{
    private readonly string _tempDir;

    public NxmHandlerBackupTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-backup-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string BackupPath => Path.Combine(_tempDir, "backup.json");

    // ------------------------------------------------------------------
    //  JSON roundtrip
    // ------------------------------------------------------------------

    [Fact]
    public void Roundtrip_FullBackup_PreservesAllFields()
    {
        var original = new NxmHandlerBackup
        {
            PreviousCommand = "\"C:\\Vortex\\vortex.exe\" \"%1\"",
            PreviousDefaultValue = "URL:NXM Protocol",
            PreviousUrlProtocolValue = "",
            BackupTimeUtc = new DateTimeOffset(
                2026, 9, 29, 12, 0, 0, TimeSpan.Zero),
            PreviousHandlerPath = "C:\\Vortex\\vortex.exe",
        };

        var json = original.ToJson();
        var back = NxmHandlerBackup.FromJson(json);

        back.PreviousCommand.Should().Be(original.PreviousCommand);
        back.PreviousDefaultValue.Should().Be(original.PreviousDefaultValue);
        back.PreviousUrlProtocolValue.Should().Be(
            original.PreviousUrlProtocolValue);
        back.BackupTimeUtc.Should().Be(original.BackupTimeUtc);
        back.PreviousHandlerPath.Should().Be(original.PreviousHandlerPath);
    }

    [Fact]
    public void Roundtrip_PartialBackup_NullsPreserved()
    {
        var original = new NxmHandlerBackup
        {
            PreviousCommand = null,
            PreviousDefaultValue = null,
            PreviousUrlProtocolValue = null,
            BackupTimeUtc = DateTimeOffset.UtcNow,
            PreviousHandlerPath = null,
        };

        var json = original.ToJson();
        var back = NxmHandlerBackup.FromJson(json);

        back.PreviousCommand.Should().BeNull();
        back.PreviousDefaultValue.Should().BeNull();
        back.PreviousUrlProtocolValue.Should().BeNull();
        back.PreviousHandlerPath.Should().BeNull();
    }

    [Fact]
    public void ToJson_UsesCamelCase()
    {
        var backup = new NxmHandlerBackup
        {
            PreviousCommand = "cmd",
            PreviousDefaultValue = "def",
            PreviousUrlProtocolValue = "",
            BackupTimeUtc = DateTimeOffset.UtcNow,
        };

        var json = backup.ToJson();

        json.Should().Contain("\"previousCommand\"");
        json.Should().Contain("\"previousDefaultValue\"");
        json.Should().Contain("\"previousUrlProtocolValue\"");
        json.Should().Contain("\"backupTimeUtc\"");
    }

    // ------------------------------------------------------------------
    //  File I/O
    // ------------------------------------------------------------------

    [Fact]
    public void SaveAndLoad_Roundtrip()
    {
        var backup = new NxmHandlerBackup
        {
            PreviousCommand = "cmd",
            BackupTimeUtc = DateTimeOffset.UtcNow,
        };

        backup.SaveToFile(BackupPath);
        File.Exists(BackupPath).Should().BeTrue();

        var loaded = NxmHandlerBackup.LoadFromFile(BackupPath);
        loaded.Should().NotBeNull();
        loaded!.PreviousCommand.Should().Be("cmd");
    }

    [Fact]
    public void SaveToFile_CreatesParentDirectory()
    {
        var nested = Path.Combine(_tempDir, "a", "b", "backup.json");
        var backup = new NxmHandlerBackup
        {
            BackupTimeUtc = DateTimeOffset.UtcNow,
        };

        backup.SaveToFile(nested);

        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void LoadFromFile_MissingFile_ReturnsNull()
    {
        var loaded = NxmHandlerBackup.LoadFromFile(
            Path.Combine(_tempDir, "does-not-exist.json"));

        loaded.Should().BeNull();
    }

    [Fact]
    public void LoadFromFile_MalformedJson_Throws()
    {
        File.WriteAllText(BackupPath, "{ not valid json }");

        var act = () => NxmHandlerBackup.LoadFromFile(BackupPath);
        act.Should().Throw<System.Text.Json.JsonException>();
    }

    // ------------------------------------------------------------------
    //  ExtractExePathFromCommand
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("\"C:\\Vortex\\vortex.exe\" \"%1\"", "C:\\Vortex\\vortex.exe")]
    [InlineData("\"D:\\MO2\\ModOrganizer.exe\" \"%1\"",
                "D:\\MO2\\ModOrganizer.exe")]
    [InlineData("\"C:\\Program Files\\Vortex\\vortex.exe\" \"%1\"",
                "C:\\Program Files\\Vortex\\vortex.exe")]
    public void ExtractExePathFromCommand_QuotedPath_ReturnsPath(
        string command, string expected)
    {
        var path = NxmHandlerBackup.ExtractExePathFromCommand(command);
        path.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no quotes at all")]
    [InlineData("\"unterminated quote")]
    [InlineData("\"\" \"%1\"")] // пустой путь
    public void ExtractExePathFromCommand_InvalidCommand_ReturnsNull(
        string? command)
    {
        var path = NxmHandlerBackup.ExtractExePathFromCommand(command);
        path.Should().BeNull();
    }
}
