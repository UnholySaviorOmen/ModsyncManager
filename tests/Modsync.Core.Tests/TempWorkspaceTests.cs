using FluentAssertions;
using Modsync.Core.Archives.Extraction;

namespace Modsync.Core.Tests;

public class TempWorkspaceTests
{
    [Fact]
    public void Constructor_CreatesDirectory()
    {
        using var ws = new TempWorkspace();

        Directory.Exists(ws.Path).Should().BeTrue();
        ws.Path.Should().StartWith(Path.GetTempPath());
        ws.Path.Should().Contain("modsyncmanager-");
    }

    [Fact]
    public void Dispose_RemovesDirectory()
    {
        string path;
        using (var ws = new TempWorkspace())
        {
            path = ws.Path;
            File.WriteAllText(Path.Combine(path, "test.txt"), "hello");
        }

        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void MultipleWorkspaces_HaveUniquePaths()
    {
        using var a = new TempWorkspace();
        using var b = new TempWorkspace();

        a.Path.Should().NotBe(b.Path);
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var ws = new TempWorkspace();
        ws.Dispose();
        var act = () => ws.Dispose();

        act.Should().NotThrow();
    }
}
