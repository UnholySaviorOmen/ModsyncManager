using System.Runtime.InteropServices;
using FluentAssertions;
using Modsync.Platform.Nexus;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus.Tests.Protocol;

/// <summary>
/// Проверяет DI-регистрацию NxmServices.
///
/// Регрессия на баг, найденный на Шаге H: без регистрации
/// IRegistryAccessor и без явной фабрики для IProtocolRegistrar
/// контейнер падал с "No constructor for type ProtocolRegistrar".
///
/// Тесты не зависят от Windows: NullRegistryAccessor регистрируется
/// на не-Windows, WindowsRegistryAccessor — на Windows. Фабрика
/// IProtocolRegistrar в любом случае резолвится.
/// </summary>
public class DiRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModsyncNxm();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddModsyncNxm_ResolvesIRegistryAccessor()
    {
        using var sp = BuildProvider();

        var accessor = sp.GetRequiredService<IRegistryAccessor>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            accessor.Should().BeOfType<WindowsRegistryAccessor>();
        }
        else
        {
            accessor.Should().BeOfType<NullRegistryAccessor>();
        }
    }

    [Fact]
    public void AddModsyncNxm_ResolvesIProtocolRegistrar()
    {
        using var sp = BuildProvider();

        var registrar = sp.GetRequiredService<IProtocolRegistrar>();

        registrar.Should().BeOfType<ProtocolRegistrar>();
    }

    [Fact]
    public void AddModsyncNxm_ResolvesINxmUrlReceiver()
    {
        using var sp = BuildProvider();

        sp.GetRequiredService<INxmUrlReceiver>()
            .Should().BeOfType<NxmUrlReceiver>();
    }

    [Fact]
    public void AddModsyncNxm_ResolvesIUrlOpener()
    {
        using var sp = BuildProvider();

        sp.GetRequiredService<IUrlOpener>()
            .Should().BeOfType<ShellUrlOpener>();
    }

    [Fact]
    public void AddModsyncNxm_ReplacesNexusFreeNxmProvider()
    {
        // Симулируем порядок GUI: сначала Install (ставит Null),
        // потом Nxm (Replace-ит на реальную реализацию).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<INexusFreeNxmProvider, NullNexusFreeNxmProvider>();
        services.AddModsyncNxm();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<INexusFreeNxmProvider>()
            .Should().BeOfType<NexusFreeNxmProvider>();
    }

    [Fact]
    public void AddModsyncNxm_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModsyncNxm();
        services.AddModsyncNxm();

        using var sp = services.BuildServiceProvider();

        // Резолвится без «multiple constructors» и без дублей.
        sp.GetRequiredService<IProtocolRegistrar>()
            .Should().BeOfType<ProtocolRegistrar>();
        sp.GetRequiredService<IRegistryAccessor>()
            .Should().NotBeNull();
    }

    [Fact]
    public void NullRegistryAccessor_AllMethodsThrow()
    {
        var accessor = new NullRegistryAccessor();

        var acts = new Action[]
        {
            () => accessor.GetValue("k", null),
            () => accessor.SetValue("k", null, "v"),
            () => accessor.DeleteKey("k", true),
            () => accessor.DeleteValue("k", null),
            () => accessor.KeyExists("k"),
        };

        foreach (var act in acts)
        {
            act.Should().Throw<PlatformNotSupportedException>(
                "реестр Windows недоступен вне Windows");
        }
    }
}
