using FluentAssertions;
using Modsync.Gui.Shared;

namespace Modsync.Gui.Shared.Tests;

public class SingleInstanceTests
{
    // Уникальное имя на каждый тест — изоляция. Иначе тесты,
    // запущенные параллельно в одном процессе xUnit, видят mutex
    // друг друга.
    private static string UniqueName()
        => "ModsyncManager.Tests.SingleInstance." + Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstInstance_IsFirstInstance_True()
    {
        using var instance = new SingleInstance(UniqueName());
        instance.IsFirstInstance.Should().BeTrue();
    }

    [Fact]
    public void SecondInstance_SameName_IsFirstInstance_False()
    {
        var name = UniqueName();

        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        first.IsFirstInstance.Should().BeTrue();
        second.IsFirstInstance.Should().BeFalse();
    }

    [Fact]
    public void AfterFirstDisposed_ThirdInstance_IsFirstInstance_True()
    {
        var name = UniqueName();

        using (var first = new SingleInstance(name))
        {
            first.IsFirstInstance.Should().BeTrue();
        }

        using var third = new SingleInstance(name);
        third.IsFirstInstance.Should().BeTrue();
    }

    [Fact]
    public void DifferentNames_BothAreFirstInstance()
    {
        using var a = new SingleInstance(UniqueName());
        using var b = new SingleInstance(UniqueName());

        a.IsFirstInstance.Should().BeTrue();
        b.IsFirstInstance.Should().BeTrue();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var instance = new SingleInstance(UniqueName());

        instance.Dispose();
        var act = () => instance.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void SecondInstance_Dispose_DoesNotReleaseFirstsMutex()
    {
        var name = UniqueName();

        using var first = new SingleInstance(name);
        var second = new SingleInstance(name);
        second.IsFirstInstance.Should().BeFalse();

        // Dispose второго не должен освободить mutex первого.
        second.Dispose();

        using var third = new SingleInstance(name);
        third.IsFirstInstance.Should().BeFalse(
            "first всё ещё держит mutex");

        // first освободится в using-дисконнекте.
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyName_Throws(string? name)
    {
        var act = () => new SingleInstance(name!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultMutexName_IsNotEmpty_NoBackslash()
    {
        SingleInstance.DefaultMutexName.Should().NotBeNullOrWhiteSpace();
        SingleInstance.DefaultMutexName.Should().NotContain("\\");
    }
}
