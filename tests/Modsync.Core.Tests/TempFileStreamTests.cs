using FluentAssertions;
using Modsync.Core.Archives;

namespace Modsync.Core.Tests;

public class TempFileStreamTests
{
    [Fact]
    public void NewStream_IsEmpty()
    {
        using var s = new TempFileStream();
        s.Length.Should().Be(0);
        s.Position.Should().Be(0);
    }

    [Fact]
    public void WriteThenRead_Works()
    {
        using var s = new TempFileStream();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        s.Write(data, 0, data.Length);
        s.Position = 0;

        var read = new byte[data.Length];
        var n = s.Read(read, 0, read.Length);

        n.Should().Be(data.Length);
        read.Should().Equal(data);
    }

    [Fact]
    public void SeekAndOverwrite_Works()
    {
        using var s = new TempFileStream();
        s.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
        s.Position = 2;
        s.Write(new byte[] { 99 }, 0, 1);
        s.Position = 0;

        var read = new byte[5];
        s.ReadExactly(read, 0, 5);

        read.Should().Equal(new byte[] { 1, 2, 99, 4, 5 });
    }

    [Fact]
    public async Task AsyncWriteThenRead_Works()
    {
        await using var s = new TempFileStream();
        var data = new byte[] { 10, 20, 30 };

        await s.WriteAsync(data, 0, data.Length);
        s.Position = 0;

        var read = new byte[data.Length];
        var n = await s.ReadAsync(read, 0, read.Length);

        n.Should().Be(data.Length);
        read.Should().Equal(data);
    }

    [Fact]
    public void Dispose_RemovesFile()
    {
        string path;
        using (var s = new TempFileStream())
        {
            path = s.TempPath;
            s.Write(new byte[] { 1, 2, 3 }, 0, 3);
            File.Exists(path).Should().BeTrue();
        }
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_RemovesFile()
    {
        string path;
        await using (var s = new TempFileStream())
        {
            path = s.TempPath;
            await s.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3);
        }
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void MultipleStreams_HaveUniquePaths()
    {
        using var a = new TempFileStream();
        using var b = new TempFileStream();

        a.TempPath.Should().NotBe(b.TempPath);
    }

    [Fact]
    public void CanSeekAndGetLength()
    {
        using var s = new TempFileStream();
        s.Write(new byte[1024], 0, 1024);

        s.Length.Should().Be(1024);
        s.Seek(512, SeekOrigin.Begin).Should().Be(512);
        s.Seek(10, SeekOrigin.Current).Should().Be(522);
        s.Seek(-22, SeekOrigin.End).Should().Be(1002);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var s = new TempFileStream();
        s.Dispose();
        var act = () => s.Dispose();
        act.Should().NotThrow();
    }
}
