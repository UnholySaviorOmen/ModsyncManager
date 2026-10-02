using FluentAssertions;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests;

public class NexusApiKeyProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _keyPath;

    public NexusApiKeyProviderTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-nexus-key-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _keyPath = Path.Combine(_tempDir, "nexus.key");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private NexusApiKeyProvider MakeProvider()
        => new(NullLogger<NexusApiKeyProvider>.Instance, _keyPath);

    [Fact]
    public void FileMissing_ReturnsNull()
    {
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().BeNull();
    }

    [Fact]
    public void FileEmpty_ReturnsNull()
    {
        File.WriteAllText(_keyPath, "");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().BeNull();
    }

    [Fact]
    public void FileWhitespaceOnly_ReturnsNull()
    {
        File.WriteAllText(_keyPath, "   \r\n\t  ");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().BeNull();
    }

    [Fact]
    public void SimpleKey_ReturnsKey()
    {
        File.WriteAllText(_keyPath, "abc123def456");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123def456");
    }

    [Fact]
    public void KeyWithTrailingNewline_ReturnsTrimmedKey()
    {
        File.WriteAllText(_keyPath, "abc123\r\n");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123");
    }

    [Fact]
    public void KeyWithLeadingAndTrailingSpaces_ReturnsTrimmedKey()
    {
        File.WriteAllText(_keyPath, "   abc123   ");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123");
    }

    [Fact]
    public void FileWithBom_ReturnsKeyWithoutBom()
    {
        // \uFEFF — BOM
        File.WriteAllText(_keyPath, "\uFEFFabc123");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123");
    }

    [Fact]
    public void FileWithBomAndNewline_ReturnsKeyWithoutBom()
    {
        File.WriteAllText(_keyPath, "\uFEFFabc123\r\n");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123");
    }

    [Fact]
    public void MultipleLines_ReturnsFirstNonEmpty()
    {
        File.WriteAllText(_keyPath, "abc123\r\nignored second line");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123");
    }

    [Fact]
    public void EmptyFirstLine_ReturnsSecondLine()
    {
        File.WriteAllText(_keyPath, "\r\nabc123");
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be("abc123");
    }

    [Fact]
    public void RealisticNexusKey_ReturnsAsIs()
    {
        // Nexus API-ключи выглядят примерно так: 32-символьный hex/base64.
        const string key = "a1b2c3d4e5f60718293a4b5c6d7e8f90";
        File.WriteAllText(_keyPath, key);
        var provider = MakeProvider();
        provider.TryGetApiKey().Should().Be(key);
    }

    [Fact]
    public void KeyFilePath_IsExposedForDiagnostics()
    {
        var provider = MakeProvider();
        provider.KeyFilePath.Should().Be(_keyPath);
    }

    [Fact]
    public void FileUnreadable_ReturnsNullWithoutThrowing()
    {
        // Открываем файл с эксклюзивным доступом — read не пройдёт.
        File.WriteAllText(_keyPath, "abc123");

        using var handle = new FileStream(
            _keyPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var provider = MakeProvider();
        var act = () => provider.TryGetApiKey();

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Save
    // ------------------------------------------------------------------

    [Fact]
    public void Save_ValidKey_WritesFile()
    {
        var provider = MakeProvider();
        provider.Save("abcdef123456");

        File.Exists(_keyPath).Should().BeTrue();
        File.ReadAllText(_keyPath).Should().Be("abcdef123456");
    }

    [Fact]
    public void Save_NoBom_NoTrailingNewline()
    {
        var provider = MakeProvider();
        provider.Save("abcdef123456");

        var bytes = File.ReadAllBytes(_keyPath);
        var startsWithBom =
            bytes.Length >= 3
            && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        startsWithBom.Should().BeFalse();

        bytes.Should().NotContain((byte)'\r');
        bytes.Should().NotContain((byte)'\n');
    }

    [Fact]
    public void Save_TrimsWhitespace()
    {
        var provider = MakeProvider();
        provider.Save("  abcdef123456  \r\n");

        File.ReadAllText(_keyPath).Should().Be("abcdef123456");
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var nested = Path.Combine(_tempDir, "a", "b", "c", "nexus.key");
        var provider = new NexusApiKeyProvider(
            NullLogger<NexusApiKeyProvider>.Instance, nested);

        provider.Save("abcdef");

        Directory.Exists(Path.GetDirectoryName(nested)).Should().BeTrue();
        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var provider = MakeProvider();
        File.WriteAllText(_keyPath, "old-key");

        provider.Save("new-key");

        File.ReadAllText(_keyPath).Should().Be("new-key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n")]
    [InlineData(null)]
    public void Save_EmptyKey_Throws(string? key)
    {
        var provider = MakeProvider();
        var act = () => provider.Save(key!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Save_ThenTryGet_ReturnsSameKey()
    {
        var provider = MakeProvider();
        provider.Save("abcdef123456");

        provider.TryGetApiKey().Should().Be("abcdef123456");
    }

    // ------------------------------------------------------------------
    //  Clear
    // ------------------------------------------------------------------

    [Fact]
    public void Clear_RemovesFile()
    {
        var provider = MakeProvider();
        provider.Save("abcdef123456");

        provider.Clear();

        File.Exists(_keyPath).Should().BeFalse();
        provider.TryGetApiKey().Should().BeNull();
    }

    [Fact]
    public void Clear_NoFile_DoesNotThrow()
    {
        var provider = MakeProvider();
        var act = () => provider.Clear();
        act.Should().NotThrow();
    }
}
