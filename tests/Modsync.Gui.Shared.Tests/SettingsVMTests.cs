using FluentAssertions;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class SettingsVMTests
{
    private static (
        SettingsVM vm,
        FakeSettingsStore settings,
        FakeProcessLauncher launcher)
        Make()
    {
        return MakeWithSettings(new FakeSettingsStore());
    }

    private static (
        SettingsVM vm,
        FakeSettingsStore settings,
        FakeProcessLauncher launcher)
        MakeWithSettings(FakeSettingsStore settings)
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

        var vm = new SettingsVM(nexus, nexusFree, settings);

        return (vm, settings, launcher);
    }

    // ------------------------------------------------------------------
    //  About
    // ------------------------------------------------------------------

    [Fact]
    public void ProductName_IsModsyncManager()
    {
        var (vm, _, _) = Make();
        vm.ProductName.Should().Be("ModsyncManager");
    }

    [Fact]
    public void Version_IsNotEmpty()
    {
        var (vm, _, _) = Make();
        vm.Version.Should().NotBeNullOrWhiteSpace();
        vm.Version.Should().NotBe("0.0.0");
    }

    [Fact]
    public void Copyright_Is2026UnholySaviorOmen()
    {
        var (vm, _, _) = Make();
        vm.Copyright.Should().Be("Copyright (C) 2026 UnholySaviorOmen");
    }

    [Fact]
    public void License_IsGpl3Only()
    {
        var (vm, _, _) = Make();
        vm.License.Should().Be("GPL-3.0-only");
    }

    [Fact]
    public void Footer_ContainsAllParts()
    {
        var (vm, _, _) = Make();
        vm.Footer.Should().Contain("ModsyncManager");
        vm.Footer.Should().Contain(vm.Version);
        vm.Footer.Should().Contain("GPL-3.0-only");
        vm.Footer.Should().Contain("Copyright (C) 2026 UnholySaviorOmen");
    }

    // ------------------------------------------------------------------
    //  Nexus
    // ------------------------------------------------------------------

    [Fact]
    public void Nexus_IsNotNull()
    {
        var (vm, _, _) = Make();
        vm.Nexus.Should().NotBeNull();
    }

    [Fact]
    public void NexusFreeDownload_IsNotNull()
    {
        var (vm, _, _) = Make();
        vm.NexusFreeDownload.Should().NotBeNull();
    }

    // ------------------------------------------------------------------
    //  DevMode
    // ------------------------------------------------------------------

    [Fact]
    public void IsDevMode_DefaultsToFalse()
    {
        var (vm, _, _) = Make();
        vm.IsDevMode.Should().BeFalse();
    }

    [Fact]
    public void IsDevMode_ReadsInitialValueFromStore()
    {
        var settings = new FakeSettingsStore();
        settings.Current.DevMode = true;

        var (vm, _, _) = MakeWithSettings(settings);

        vm.IsDevMode.Should().BeTrue();
    }

    [Fact]
    public void IsDevMode_Set_UpdatesStoreAndSaves()
    {
        var (vm, settings, _) = Make();

        vm.IsDevMode = true;

        settings.Current.DevMode.Should().BeTrue();
        settings.SaveCallCount.Should().Be(1);
    }

    [Fact]
    public void IsDevMode_SetSameValue_DoesNotSave()
    {
        var (vm, settings, _) = Make();
        vm.IsDevMode = false;

        settings.SaveCallCount.Should().Be(0);
    }

    [Fact]
    public void IsDevMode_ToggledTwice_SavesTwice()
    {
        var (vm, settings, _) = Make();

        vm.IsDevMode = true;
        vm.IsDevMode = false;

        settings.SaveCallCount.Should().Be(2);
        settings.Current.DevMode.Should().BeFalse();
    }

    [Fact]
    public void IsDevMode_Set_RaisesPropertyChanged()
    {
        var (vm, _, _) = Make();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsDevMode = true;

        changed.Should().Contain(nameof(SettingsVM.IsDevMode));
    }
}
