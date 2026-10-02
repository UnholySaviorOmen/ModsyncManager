using FluentAssertions;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class NexusFreeDownloadSettingsVMTests
{
    private static (NexusFreeDownloadSettingsVM vm, FakeProtocolRegistrar registrar)
        Make()
    {
        var registrar = new FakeProtocolRegistrar();

        var vm = new NexusFreeDownloadSettingsVM(
            registrar,
            NullLogger<NexusFreeDownloadSettingsVM>.Instance);

        return (vm, registrar);
    }

    // ------------------------------------------------------------------
    //  Initial state
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_NotRegistered()
    {
        var (vm, _) = Make();

        vm.State.Should().Be(ProtocolRegistrationState.NotRegistered);
        vm.IsNotRegistered.Should().BeTrue();
        vm.IsRegisteredToUs.Should().BeFalse();
        vm.IsRegisteredToOther.Should().BeFalse();
        vm.IsUnknown.Should().BeFalse();
        vm.ErrorMessage.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Refresh
    // ------------------------------------------------------------------

    [Fact]
    public async Task Refresh_NotRegistered_UpdatesState()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.NotRegistered;

        await vm.RefreshAsync();

        vm.State.Should().Be(ProtocolRegistrationState.NotRegistered);
        registrar.GetStateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Refresh_RegisteredToUs_UpdatesState()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.RegisteredToUs;

        await vm.RefreshAsync();

        vm.State.Should().Be(ProtocolRegistrationState.RegisteredToUs);
        vm.IsRegisteredToUs.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_RegisteredToOther_UpdatesState()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.RegisteredToOther;

        await vm.RefreshAsync();

        vm.State.Should().Be(ProtocolRegistrationState.RegisteredToOther);
        vm.IsRegisteredToOther.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_Unknown_UpdatesState()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.Unknown;

        await vm.RefreshAsync();

        vm.State.Should().Be(ProtocolRegistrationState.Unknown);
        vm.IsUnknown.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Register
    // ------------------------------------------------------------------

    [Fact]
    public async Task Register_Success_RefreshesState()
    {
        var (vm, registrar) = Make();

        // После успешной регистрации состояние — RegisteredToUs.
        registrar.RegisterResult =
            ProtocolRegistrationResult.Registered();
        registrar.State = ProtocolRegistrationState.RegisteredToUs;

        await vm.RegisterCommand.ExecuteAsync(null);

        registrar.RegisterCallCount.Should().Be(1);
        vm.State.Should().Be(ProtocolRegistrationState.RegisteredToUs);
        vm.ErrorMessage.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Register_HandlerNotFound_SetsErrorMessage()
    {
        var (vm, registrar) = Make();

        registrar.RegisterResult =
            ProtocolRegistrationResult.HandlerNotFound(
                @"C:\fake\ModsyncManager.NxmHandler.exe");

        await vm.RegisterCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("ModsyncManager.NxmHandler.exe");
        vm.ErrorMessage.Should().Contain("Reinstall ModsyncManager");
    }

    [Fact]
    public async Task Register_BackupFailed_SetsErrorMessage()
    {
        var (vm, registrar) = Make();

        registrar.RegisterResult =
            ProtocolRegistrationResult.BackupFailed("disk full");

        await vm.RegisterCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("disk full");
    }

    [Fact]
    public async Task Register_RegistryWriteFailed_SetsErrorMessage()
    {
        var (vm, registrar) = Make();

        registrar.RegisterResult =
            ProtocolRegistrationResult.RegistryWriteFailed("access denied");

        await vm.RegisterCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("access denied");
    }

    [Fact]
    public async Task Register_RefreshesAfterCompletion()
    {
        var (vm, registrar) = Make();
        registrar.RegisterResult = ProtocolRegistrationResult.Registered();

        await vm.RegisterCommand.ExecuteAsync(null);

        // После RegisterCommand должен быть вызов RefreshAsync.
        registrar.GetStateCallCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Restore
    // ------------------------------------------------------------------

    [Fact]
    public async Task Restore_Success_RefreshesState()
    {
        var (vm, registrar) = Make();

        registrar.RestoreResult = ProtocolRegistrationResult.Restored();
        registrar.State = ProtocolRegistrationState.NotRegistered;

        await vm.RestoreCommand.ExecuteAsync(null);

        registrar.RestoreCallCount.Should().Be(1);
        vm.State.Should().Be(ProtocolRegistrationState.NotRegistered);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Restore_AlreadyRestored_NoError()
    {
        var (vm, registrar) = Make();

        registrar.RestoreResult =
            ProtocolRegistrationResult.AlreadyRestored();
        registrar.State = ProtocolRegistrationState.NotRegistered;

        await vm.RestoreCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().BeNull();
        vm.State.Should().Be(ProtocolRegistrationState.NotRegistered);
    }

    [Fact]
    public async Task Restore_RegistryWriteFailed_SetsErrorMessage()
    {
        var (vm, registrar) = Make();

        registrar.RestoreResult =
            ProtocolRegistrationResult.RegistryWriteFailed("permission denied");

        await vm.RestoreCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("permission denied");
    }

    // ------------------------------------------------------------------
    //  StatusText / StatusColor
    // ------------------------------------------------------------------

    [Fact]
    public async Task StatusText_RegisteredToUs_ContainsEnabled()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.RegisteredToUs;
        await vm.RefreshAsync();

        vm.StatusText.Should().Contain("enabled");
    }

    [Fact]
    public async Task StatusText_RegisteredToOther_ContainsWarning()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.RegisteredToOther;
        await vm.RefreshAsync();

        vm.StatusText.Should().Contain("Another app");
        vm.StatusText.Should().Contain("restore");
    }

    [Fact]
    public async Task StatusColor_RegisteredToUs_IsGreen()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.RegisteredToUs;
        await vm.RefreshAsync();

        vm.StatusColor.Should().Be("#7fc98a");
    }

    [Fact]
    public async Task StatusColor_RegisteredToOther_IsWarm()
    {
        var (vm, registrar) = Make();
        registrar.State = ProtocolRegistrationState.RegisteredToOther;
        await vm.RefreshAsync();

        vm.StatusColor.Should().Be("#d3b181");
    }

    // ------------------------------------------------------------------
    //  HandlerPath
    // ------------------------------------------------------------------

    [Fact]
    public void HandlerPath_ContainsExeName()
    {
        var (vm, _) = Make();
        vm.HandlerPath.Should().EndWith("ModsyncManager.NxmHandler.exe");
    }
}
