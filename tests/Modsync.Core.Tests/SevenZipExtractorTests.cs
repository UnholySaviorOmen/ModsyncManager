using System.IO.Compression;
using FluentAssertions;
using Modsync.Core.Archives.Extraction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Core.Tests;

public class SevenZipExtractorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SevenZipExtractor _extractor;

    public SevenZipExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "modsyncmanager-7z-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _extractor = new SevenZipExtractor(NullLogger<SevenZipExtractor>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateZip(string name, params (string entryPath, byte[] content)[] files)
    {
        var zipPath = Path.Combine(_tempDir, name);
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var (entryPath, content) in files)
        {
            var entry = zip.CreateEntry(entryPath);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }

        return zipPath;
    }

    private string CreateExtractDir(string name = "out")
    {
        var dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Constructor_ResolvesSevenZipPath()
    {
        // 7z.exe должен найтись рядом с тестовой сборкой в Assets/7z/.
        var path = _extractor.SevenZipExePath;

        path.Should().EndWith(Path.Combine("Assets", "7z", "7z.exe"));
        File.Exists(path).Should().BeTrue(
            $"7z.exe should be copied to test output at: {path}");
    }

    [Fact]
    public void CanExtract_ZipFile_ReturnsTrue()
    {
        var zip = CreateZip("test.zip", ("file.txt", new byte[] { 1 }));
        _extractor.CanExtract(zip).Should().BeTrue();
    }

    [Fact]
    public void CanExtract_NonArchive_ReturnsFalse()
    {
        var txt = Path.Combine(_tempDir, "readme.txt");
        File.WriteAllText(txt, "hello");
        _extractor.CanExtract(txt).Should().BeFalse();
    }

    [Fact]
    public async Task Extract_SimpleZip_ExtractsAllFiles()
    {
        var zip = CreateZip("simple.zip",
            ("file1.txt", System.Text.Encoding.UTF8.GetBytes("content1")),
            ("dir/file2.txt", System.Text.Encoding.UTF8.GetBytes("content2")));

        var dest = CreateExtractDir();

        var extracted = await _extractor.ExtractAsync(zip, dest, CancellationToken.None);

        extracted.Should().HaveCount(2);
        extracted.Should().Contain("file1.txt");
        extracted.Should().Contain("dir/file2.txt");

        File.ReadAllText(Path.Combine(dest, "file1.txt")).Should().Be("content1");
        File.ReadAllText(Path.Combine(dest, "dir", "file2.txt")).Should().Be("content2");
    }

    [Fact]
    public async Task Extract_NestedDirectories_CreatesThem()
    {
        var zip = CreateZip("nested.zip",
            ("a/b/c/deep.txt", System.Text.Encoding.UTF8.GetBytes("deep")));

        var dest = CreateExtractDir();
        await _extractor.ExtractAsync(zip, dest, CancellationToken.None);

        File.Exists(Path.Combine(dest, "a", "b", "c", "deep.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Extract_EmptyArchive_ReturnsEmptyList()
    {
        var zip = CreateZip("empty.zip");

        var dest = CreateExtractDir();
        var extracted = await _extractor.ExtractAsync(zip, dest, CancellationToken.None);

        extracted.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_NonExistentArchive_Throws()
    {
        var dest = CreateExtractDir();

        var act = async () => await _extractor.ExtractAsync(
            Path.Combine(_tempDir, "missing.zip"), dest, CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Extract_NonExistentDestination_Throws()
    {
        var zip = CreateZip("test.zip", ("file.txt", new byte[] { 1 }));

        var act = async () => await _extractor.ExtractAsync(
            zip, Path.Combine(_tempDir, "no-such-dir"), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task Extract_BinaryContent_PreservesBytes()
    {
        // Бинарный контент с нулями и высокими байтами — проверяем, что 7z не портит.
        var binaryContent = new byte[256];
        for (int i = 0; i < 256; i++) binaryContent[i] = (byte)i;

        var zip = CreateZip("binary.zip", ("data.bin", binaryContent));

        var dest = CreateExtractDir();
        await _extractor.ExtractAsync(zip, dest, CancellationToken.None);

        var extractedBytes = File.ReadAllBytes(Path.Combine(dest, "data.bin"));
        extractedBytes.Should().Equal(binaryContent);
    }

    [Fact]
    public async Task Extract_UnicodeFileName_PreservesName()
    {
        var zip = CreateZip("unicode.zip",
            ("Мод/файл.txt", System.Text.Encoding.UTF8.GetBytes("hi")));

        var dest = CreateExtractDir();
        var extracted = await _extractor.ExtractAsync(zip, dest, CancellationToken.None);

        extracted.Should().Contain("Мод/файл.txt");
        File.Exists(Path.Combine(dest, "Мод", "файл.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Extract_SpaceInFileName_Works()
    {
        var zip = CreateZip("space.zip",
            ("folder with spaces/file with spaces.txt",
             System.Text.Encoding.UTF8.GetBytes("data")));

        var dest = CreateExtractDir();
        var extracted = await _extractor.ExtractAsync(zip, dest, CancellationToken.None);

        extracted.Should().HaveCount(1);
        File.Exists(Path.Combine(dest, "folder with spaces", "file with spaces.txt"))
            .Should().BeTrue();
    }
}
