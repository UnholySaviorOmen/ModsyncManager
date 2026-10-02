// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Pack;

namespace Modsync.Pack.Tests;

public class PackInputFactoryTests
{
    [Fact]
    public void Create_NormalizesConfigPathToFullPath()
    {
        var relative = "subdir/modsyncmanager-pack.json";

        var input = PackInputFactory.Create(relative);

        input.ConfigPath.Should().Be(Path.GetFullPath(relative));
        Path.IsPathRooted(input.ConfigPath).Should().BeTrue();
    }

    [Fact]
    public void Create_WithNullParallelOptions_UsesDefault()
    {
        var input = PackInputFactory.Create("x.json");

        input.ParallelOptions.Should().NotBeNull();
        input.ParallelOptions.MaxDegreeOfParallelism
            .Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Create_WithExplicitParallelOptions_PassesThemThrough()
    {
        var opts = new ParallelOptions { MaxDegreeOfParallelism = 3 };

        var input = PackInputFactory.Create("x.json", parallelOptions: opts);

        input.ParallelOptions.Should().BeSameAs(opts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_EmptyConfigPath_Throws(string? path)
    {
        var act = () => PackInputFactory.Create(path!);
        act.Should().Throw<ArgumentException>();
    }
}
