using FluentAssertions;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class CacheVMTests
{
    private static (CacheVM vm, FakeHashCache cache) Make()
    {
        var cache = new FakeHashCache();
        var vm = new CacheVM(
            cache,
            NullLogger<CacheVM>.Instance);
        return (vm, cache);
    }

    [Fact]
    public void HashCachePath_EndsWithCacheDb()
    {
        var (vm, _) = Make();
        vm.HashCachePath.Should().EndWith("cache.db");
    }

    [Fact]
    public void HashCachePath_ContainsModsyncManager()
    {
        var (vm, _) = Make();
        vm.HashCachePath.Should().Contain("ModsyncManager");
    }

    [Fact]
    public void InitialState_NoStatusMessage()
    {
        var (vm, _) = Make();
        vm.StatusMessage.Should().BeNull();
    }

    [Fact]
    public void InitialState_SizeTextIsNotEmpty()
    {
        var (vm, _) = Make();
        vm.HashCacheSizeText.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ClearHashCache_CallsCacheClear()
    {
        var (vm, cache) = Make();
        vm.ClearHashCacheCommand.Execute(null);

        cache.ClearCallCount.Should().Be(1);
    }

    [Fact]
    public void ClearHashCache_SetsStatusMessage()
    {
        var (vm, _) = Make();
        vm.ClearHashCacheCommand.Execute(null);

        vm.StatusMessage.Should().Be("Hash cache cleared.");
    }

    [Fact]
    public void ClearHashCache_CacheThrows_SetsErrorMessage()
    {
        var (vm, cache) = Make();
        cache.ClearException = new InvalidOperationException("boom");

        vm.ClearHashCacheCommand.Execute(null);

        vm.StatusMessage.Should().Contain("boom");
    }

    [Fact]
    public void ClearHashCache_CalledTwice_Works()
    {
        var (vm, cache) = Make();
        vm.ClearHashCacheCommand.Execute(null);
        vm.ClearHashCacheCommand.Execute(null);

        cache.ClearCallCount.Should().Be(2);
    }
}
