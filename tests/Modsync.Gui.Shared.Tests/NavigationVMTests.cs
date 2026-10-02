using FluentAssertions;
using Modsync.Gui.Shared.Navigation;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class NavigationVMTests
{
    private static (NavigationVM vm, SettingsVM settings, List<ScreenType> navigated)
        Make()
    {
        var settings = MakeSettings();
        var navigated = new List<ScreenType>();
        var vm = new NavigationVM(s => navigated.Add(s), settings);
        return (vm, settings, navigated);
    }

    private static SettingsVM MakeSettings()
    {
        var launcher = new FakeProcessLauncher();

        var nexus = new NexusSettingsVM(
            new FakeNexusApiKeyProvider(),
            new FakeNexusCredentialValidator(),
            launcher,
            NullLogger<NexusSettingsVM>.Instance);

        var nexusFree = new NexusFreeDownloadSettingsVM(
            new FakeProtocolRegistrar(),
            NullLogger<NexusFreeDownloadSettingsVM>.Instance);

        return new SettingsVM(
            nexus,
            nexusFree,
            new FakeSettingsStore());
    }

    // ------------------------------------------------------------------
    //  DevMode = false (default)
    // ------------------------------------------------------------------

    [Fact]
    public void Default_ShowsOnlyHomeAndSettings()
    {
        var (vm, _, _) = Make();

        vm.Items.Should().HaveCount(2);
        vm.Items.Select(i => i.Screen).Should().Equal(
            ScreenType.Home,
            ScreenType.Settings);
    }

    [Fact]
    public void Default_NoNavigationItemsForInstallPackVerifyLogsCache()
    {
        var (vm, _, _) = Make();

        vm.Items.Should().NotContain(i => i.Screen == ScreenType.Install);
        vm.Items.Should().NotContain(i => i.Screen == ScreenType.Pack);
        vm.Items.Should().NotContain(i => i.Screen == ScreenType.Verify);
        vm.Items.Should().NotContain(i => i.Screen == ScreenType.Logs);
        vm.Items.Should().NotContain(i => i.Screen == ScreenType.Cache);
    }

    // ------------------------------------------------------------------
    //  DevMode = true
    // ------------------------------------------------------------------

    [Fact]
    public void DevModeOn_ShowsAllSevenItems()
    {
        var (vm, settings, _) = Make();

        settings.IsDevMode = true;

        vm.Items.Should().HaveCount(7);
        vm.Items.Select(i => i.Screen).Should().Equal(
            ScreenType.Home,
            ScreenType.Install,
            ScreenType.Pack,
            ScreenType.Verify,
            ScreenType.Logs,
            ScreenType.Cache,
            ScreenType.Settings);
    }

    [Fact]
    public void DevModeOnThenOff_BackToTwoItems()
    {
        var (vm, settings, _) = Make();

        settings.IsDevMode = true;
        vm.Items.Should().HaveCount(7);

        settings.IsDevMode = false;
        vm.Items.Should().HaveCount(2);
        vm.Items.Select(i => i.Screen).Should().Equal(
            ScreenType.Home,
            ScreenType.Settings);
    }

    // ------------------------------------------------------------------
    //  SelectedItem сохраняется при перестройке
    // ------------------------------------------------------------------

    [Fact]
    public void DevModeOn_KeepsSelectedHome()
    {
        var (vm, settings, _) = Make();

        vm.SelectScreen(ScreenType.Home);
        settings.IsDevMode = true;

        vm.SelectedItem!.Screen.Should().Be(ScreenType.Home);
    }

    [Fact]
    public void DevModeOff_FromInstall_SwitchesToHome_AndNavigates()
    {
        var (vm, settings, navigated) = Make();

        settings.IsDevMode = true;
        vm.SelectScreen(ScreenType.Install);
        navigated.Clear();

        settings.IsDevMode = false;

        vm.SelectedItem!.Screen.Should().Be(ScreenType.Home);
        navigated.Should().ContainSingle().Which.Should().Be(ScreenType.Home);
    }

    [Fact]
    public void DevModeOff_FromCache_SwitchesToHome()
    {
        var (vm, settings, navigated) = Make();

        settings.IsDevMode = true;
        vm.SelectScreen(ScreenType.Cache);
        navigated.Clear();

        settings.IsDevMode = false;

        vm.SelectedItem!.Screen.Should().Be(ScreenType.Home);
        navigated.Should().ContainSingle().Which.Should().Be(ScreenType.Home);
    }

    [Fact]
    public void DevModeOff_FromSettings_KeepsSettings()
    {
        var (vm, settings, navigated) = Make();

        settings.IsDevMode = true;
        vm.SelectScreen(ScreenType.Settings);
        navigated.Clear();

        settings.IsDevMode = false;

        vm.SelectedItem!.Screen.Should().Be(ScreenType.Settings);
        navigated.Should().BeEmpty();
    }

    [Fact]
    public void DevModeOff_FromPack_SwitchesToHome()
    {
        var (vm, settings, navigated) = Make();

        settings.IsDevMode = true;
        vm.SelectScreen(ScreenType.Pack);
        navigated.Clear();

        settings.IsDevMode = false;

        vm.SelectedItem!.Screen.Should().Be(ScreenType.Home);
        navigated.Should().ContainSingle().Which.Should().Be(ScreenType.Home);
    }

    // ------------------------------------------------------------------
    //  SelectScreen
    // ------------------------------------------------------------------

    [Fact]
    public void SelectScreen_KnownScreen_SetsSelectedItem_WithoutCallback()
    {
        var (vm, settings, navigated) = Make();
        settings.IsDevMode = true;

        navigated.Clear();

        vm.SelectScreen(ScreenType.Pack);

        vm.SelectedItem!.Screen.Should().Be(ScreenType.Pack);
        navigated.Should().BeEmpty("SelectScreen должен подавлять callback");
    }

    [Fact]
    public void SelectScreen_HiddenScreen_DoesNothing()
    {
        var (vm, _, navigated) = Make();
        // DevMode = false, Install скрыт.

        vm.SelectScreen(ScreenType.Install);

        vm.SelectedItem.Should().BeNull();
        navigated.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    //  Клик пользователя
    // ------------------------------------------------------------------

    [Fact]
    public void SelectedItemChange_InvokesCallback()
    {
        var (vm, _, navigated) = Make();
        var settingsItem = vm.Items.First(i => i.Screen == ScreenType.Settings);

        vm.SelectedItem = settingsItem;

        navigated.Should().ContainSingle().Which.Should().Be(ScreenType.Settings);
    }

    [Fact]
    public void SelectedItemNull_DoesNotInvokeCallback()
    {
        var (vm, _, navigated) = Make();

        vm.SelectedItem = null;

        navigated.Should().BeEmpty();
    }
}
