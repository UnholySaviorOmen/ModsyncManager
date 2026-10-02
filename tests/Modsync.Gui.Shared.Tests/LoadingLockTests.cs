using FluentAssertions;
using Modsync.Gui.Shared.ViewModels;

namespace Modsync.Gui.Shared.Tests;

public class LoadingLockTests
{
    [Fact]
    public void InitialState_NotLoading()
    {
        var @lock = new LoadingLock();
        @lock.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void SingleLock_SetsIsLoading()
    {
        var @lock = new LoadingLock();
        using (@lock.Lock())
        {
            @lock.IsLoading.Should().BeTrue();
        }
    }

    [Fact]
    public void Lock_Released_ResetsIsLoading()
    {
        var @lock = new LoadingLock();
        using (@lock.Lock()) { }
        @lock.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void NestedLocks_StayLoadingUntilLastRelease()
    {
        var @lock = new LoadingLock();
        var a = @lock.Lock();
        var b = @lock.Lock();

        @lock.IsLoading.Should().BeTrue();

        a.Dispose();
        @lock.IsLoading.Should().BeTrue();

        b.Dispose();
        @lock.IsLoading.Should().BeFalse();
    }

    [Fact]
    public void DoubleDispose_DoesNotCorruptCounter()
    {
        var @lock = new LoadingLock();
        var handle = @lock.Lock();
        handle.Dispose();
        handle.Dispose(); // idempotent

        @lock.IsLoading.Should().BeFalse();

        using (@lock.Lock())
        {
            @lock.IsLoading.Should().BeTrue();
        }
    }
}
