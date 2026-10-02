// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Install;

namespace Modsync.Install.Tests;

public class InstallInputFactoryTests
{
    [Fact]
    public void Create_NormalizesManifestPath()
    {
        var relative = "downloads/modlist.json";

        var input = InstallInputFactory.Create(relative);

        input.ManifestPath.Should().Be(Path.GetFullPath(relative));
    }

    [Fact]
    public void Create_WithoutTarget_LeavesTargetNull()
    {
        var input = InstallInputFactory.Create("modlist.json");

        input.Target.Should().BeNull();
    }

    [Fact]
    public void Create_WithEmptyTarget_LeavesTargetNull()
    {
        var input = InstallInputFactory.Create("modlist.json", target: "  ");

        input.Target.Should().BeNull();
    }

    [Fact]
    public void Create_WithTarget_NormalizesToFullPath()
    {
        var input = InstallInputFactory.Create(
            "modlist.json", target: "D:/Games/MyPack");

        input.Target.Should().Be(Path.GetFullPath("D:/Games/MyPack"));
    }

    [Fact]
    public void Create_WithNullParallelOptions_UsesDefault()
    {
        var input = InstallInputFactory.Create("modlist.json");

        input.ParallelOptions.MaxDegreeOfParallelism
            .Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Create_WithExplicitParallelOptions_PassesThemThrough()
    {
        var opts = new ParallelOptions { MaxDegreeOfParallelism = 1 };

        var input = InstallInputFactory.Create(
            "modlist.json", parallelOptions: opts);

        input.ParallelOptions.Should().BeSameAs(opts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_EmptyManifestPath_Throws(string? path)
    {
        var act = () => InstallInputFactory.Create(path!);
        act.Should().Throw<ArgumentException>();
    }
}
