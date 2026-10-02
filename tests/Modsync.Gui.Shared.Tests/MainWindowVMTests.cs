using Modsync.Core.Archives;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Platform.Nexus;
using Modsync.Platform.Nexus.Protocol;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modsync.Gui.Shared.Tests;

public class MainWindowVMTests
{
    private static ServiceProvider BuildProvider(
        FakeInstalledPackScanner? scanner = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IUiDispatcher, FakeUiDispatcher>();
        services.AddGuiShared();

        // Заменяем инфраструктурные сервисы на fake'и.
        services.RemoveAll<IInstalledPackScanner>();
        services.AddSingleton<IInstalledPackScanner>(
            scanner ?? new FakeInstalledPackScanner());

        services.RemoveAll<IFilePickerService>();
        services.AddSingleton<IFilePickerService>(new FakeFilePickerService());

        services.RemoveAll<IProcessLauncher>();
        services.AddSingleton<IProcessLauncher>(new FakeProcessLauncher());

        // SettingsStore нужен SettingsVM.
        services.AddSingleton<ISettingsStore>(new FakeSettingsStore());

        // IHashCache нужен CacheVM.
        services.RemoveAll<IHashCache>();
        services.AddSingleton<IHashCache>(new FakeHashCache());

        // Nexus-сервисы нужны для NexusSettingsVM (→ SettingsVM).
        services.AddSingleton<INexusApiKeyProvider>(
            new FakeNexusApiKeyProvider());
        services.AddSingleton<INexusCredentialValidator>(
            new FakeNexusCredentialValidator());

        // Nexus Free Download — для NexusFreeDownloadSettingsVM (→ SettingsVM).
        services.AddSingleton<IProtocolRegistrar>(
            new FakeProtocolRegistrar());

        services.AddSingleton<IScreenFactory>(sp => new FakeScreenFactory(sp));
        services.AddSingleton<MainWindowVM>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Constructor_ActivePane_IsHome()
    {
        using var sp = BuildProvider();
        var vm = sp.GetRequiredService<MainWindowVM>();

        vm.ActivePane.Should().BeOfType<HomeVM>();
        vm.Navigation.SelectedItem!.Screen.Should().Be(ScreenType.Home);
    }

    [Fact]
    public void Constructor_HomeRefreshCalled()
    {
        var scanner = new FakeInstalledPackScanner();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "Initial"));

        using var sp = BuildProvider(scanner);
        var vm = sp.GetRequiredService<MainWindowVM>();

        vm.Home.Items.Should().HaveCount(1);
        vm.Home.Items[0].Name.Should().Be("Initial");
    }

    [Fact]
    public void NavigateTo_Home_ReturnsSameInstance()
    {
        using var sp = BuildProvider();
        var vm = sp.GetRequiredService<MainWindowVM>();
        var home = vm.Home;

        vm.NavigateTo(ScreenType.Home);

        vm.ActivePane.Should().BeSameAs(home);
    }

    [Fact]
    public void NavigateTo_Home_RefreshesItems()
    {
        var scanner = new FakeInstalledPackScanner();
        using var sp = BuildProvider(scanner);
        var vm = sp.GetRequiredService<MainWindowVM>();

        vm.Home.Items.Should().BeEmpty();

        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "New Pack"));

        vm.NavigateTo(ScreenType.Home);

        vm.Home.Items.Should().HaveCount(1);
        vm.Home.Items[0].Name.Should().Be("New Pack");
    }

    [Fact]
    public void NavigateTo_SameScreenTwice_ReturnsSameInstance()
    {
        using var sp = BuildProvider();
        var vm = sp.GetRequiredService<MainWindowVM>();

        vm.NavigateTo(ScreenType.Home);
        var first = vm.ActivePane;
        vm.NavigateTo(ScreenType.Home);
        var second = vm.ActivePane;

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Navigation_ExposesSettingsVM_SameInstanceAsDI()
    {
        using var sp = BuildProvider();
        var vm = sp.GetRequiredService<MainWindowVM>();
        var settings = sp.GetRequiredService<SettingsVM>();

        settings.IsDevMode = true;

        vm.Navigation.Items.Should().HaveCount(7);
    }

    [Fact]
    public void InstallRequested_FromHome_DoesNotThrow()
    {
        var scanner = new FakeInstalledPackScanner();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack());

        using var sp = BuildProvider(scanner);
        var vm = sp.GetRequiredService<MainWindowVM>();

        var act = () => vm.Home.Items[0].InstallCommand.Execute(null);
        act.Should().NotThrow();

        vm.ActivePane.Should().BeOfType<HomeVM>();
    }
}
