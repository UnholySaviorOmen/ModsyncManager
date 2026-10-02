using FluentAssertions;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class NexusSettingsVMTests
{
    private static (
        NexusSettingsVM vm,
        FakeNexusApiKeyProvider keyProvider,
        FakeNexusCredentialValidator validator,
        FakeProcessLauncher launcher)
        Make()
    {
        var keyProvider = new FakeNexusApiKeyProvider();
        var validator = new FakeNexusCredentialValidator();
        var launcher = new FakeProcessLauncher();

        var vm = new NexusSettingsVM(
            keyProvider, validator, launcher,
            NullLogger<NexusSettingsVM>.Instance);

        return (vm, keyProvider, validator, launcher);
    }

    // ------------------------------------------------------------------
    //  Initial state
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_NotLoggedIn()
    {
        var (vm, _, _, _) = Make();

        vm.Status.Should().Be(NexusAccountStatus.NotLoggedIn);
        vm.IsLoggedIn.Should().BeFalse();
        vm.HasSavedKey.Should().BeFalse();
        vm.UserName.Should().BeNull();
        vm.IsPremium.Should().BeFalse();
        vm.ApiKeyInput.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.MaskedSavedKey.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Refresh
    // ------------------------------------------------------------------

    [Fact]
    public async Task Refresh_NoKey_SetsNotLoggedIn()
    {
        var (vm, _, _, _) = Make();
        await vm.RefreshAsync();

        vm.Status.Should().Be(NexusAccountStatus.NotLoggedIn);
        vm.HasSavedKey.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_ValidKey_SetsLoggedIn()
    {
        var (vm, keyProvider, validator, _) = Make();
        keyProvider.Key = "abcdef1234567890";
        validator.ResultToReturn = FakeNexusCredentialValidator.Valid(
            userName: "someuser", isPremium: true);

        await vm.RefreshAsync();

        vm.Status.Should().Be(NexusAccountStatus.LoggedIn);
        vm.IsLoggedIn.Should().BeTrue();
        vm.HasSavedKey.Should().BeTrue();
        vm.UserName.Should().Be("someuser");
        vm.IsPremium.Should().BeTrue();
        vm.MaskedSavedKey.Should().Be("abcd...7890");
    }

    [Fact]
    public async Task Refresh_InvalidKey_SetsInvalidSaved()
    {
        var (vm, keyProvider, validator, _) = Make();
        keyProvider.Key = "abcdef1234567890";
        validator.ResultToReturn = FakeNexusCredentialValidator.Invalid();

        await vm.RefreshAsync();

        vm.Status.Should().Be(NexusAccountStatus.InvalidSaved);
        vm.IsLoggedIn.Should().BeFalse();
        vm.HasSavedKey.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("rejected");
    }

    [Fact]
    public async Task Refresh_NetworkError_SetsNetworkError()
    {
        var (vm, keyProvider, validator, _) = Make();
        keyProvider.Key = "abcdef1234567890";
        validator.ResultToReturn = FakeNexusCredentialValidator.NetworkError();

        await vm.RefreshAsync();

        vm.Status.Should().Be(NexusAccountStatus.NetworkError);
        vm.HasSavedKey.Should().BeTrue();
        vm.ErrorMessage.Should().Contain("Network error");
    }

    [Fact]
    public async Task Refresh_ShortKey_MasksAllStars()
    {
        var (vm, keyProvider, validator, _) = Make();
        keyProvider.Key = "short";
        validator.ResultToReturn = FakeNexusCredentialValidator.Valid();

        await vm.RefreshAsync();

        vm.MaskedSavedKey.Should().Be("*****");
    }

    // ------------------------------------------------------------------
    //  SaveAndValidate
    // ------------------------------------------------------------------

    [Fact]
    public void SaveAndValidate_NoInput_CannotExecute()
    {
        var (vm, _, _, _) = Make();
        vm.SaveAndValidateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAndValidate_ValidKey_SavesAndSetsLoggedIn()
    {
        var (vm, keyProvider, validator, _) = Make();
        validator.ResultToReturn = FakeNexusCredentialValidator.Valid(
            userName: "someuser", isPremium: true);

        vm.ApiKeyInput = "abcdef1234567890";
        await vm.SaveAndValidateCommand.ExecuteAsync(null);

        keyProvider.Key.Should().Be("abcdef1234567890");
        vm.Status.Should().Be(NexusAccountStatus.LoggedIn);
        vm.UserName.Should().Be("someuser");
        vm.IsPremium.Should().BeTrue();
        vm.ApiKeyInput.Should().BeNull();
        vm.MaskedSavedKey.Should().Be("abcd...7890");
    }

    [Fact]
    public async Task SaveAndValidate_TrimsInput()
    {
        var (vm, keyProvider, validator, _) = Make();
        validator.ResultToReturn = FakeNexusCredentialValidator.Valid();

        vm.ApiKeyInput = "  abcdef1234567890  ";
        await vm.SaveAndValidateCommand.ExecuteAsync(null);

        keyProvider.Key.Should().Be("abcdef1234567890");
    }

    [Fact]
    public async Task SaveAndValidate_InvalidKey_DoesNotSave()
    {
        var (vm, keyProvider, validator, _) = Make();
        validator.ResultToReturn = FakeNexusCredentialValidator.Invalid();

        vm.ApiKeyInput = "abcdef1234567890";
        await vm.SaveAndValidateCommand.ExecuteAsync(null);

        keyProvider.Key.Should().BeNull();
        vm.Status.Should().Be(NexusAccountStatus.NotLoggedIn);
        vm.ErrorMessage.Should().Contain("rejected");
    }

    [Fact]
    public async Task SaveAndValidate_NetworkError_DoesNotSave()
    {
        var (vm, keyProvider, validator, _) = Make();
        validator.ResultToReturn = FakeNexusCredentialValidator.NetworkError();

        vm.ApiKeyInput = "abcdef1234567890";
        await vm.SaveAndValidateCommand.ExecuteAsync(null);

        keyProvider.Key.Should().BeNull();
        vm.Status.Should().Be(NexusAccountStatus.NotLoggedIn);
        vm.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAndValidate_ValidatorThrows_SetsError()
    {
        var (vm, keyProvider, validator, _) = Make();
        validator.ExceptionToThrow = new InvalidOperationException("boom");

        vm.ApiKeyInput = "abcdef1234567890";
        await vm.SaveAndValidateCommand.ExecuteAsync(null);

        keyProvider.Key.Should().BeNull();
        vm.Status.Should().Be(NexusAccountStatus.NotLoggedIn);
        vm.ErrorMessage.Should().Contain("boom");
    }

    // ------------------------------------------------------------------
    //  Logout
    // ------------------------------------------------------------------

    [Fact]
    public async Task Logout_WithKey_RemovesKeyAndResets()
    {
        var (vm, keyProvider, validator, _) = Make();
        keyProvider.Key = "abcdef1234567890";
        validator.ResultToReturn = FakeNexusCredentialValidator.Valid();

        await vm.RefreshAsync();
        vm.IsLoggedIn.Should().BeTrue();

        vm.LogoutCommand.Execute(null);

        keyProvider.Key.Should().BeNull();
        vm.Status.Should().Be(NexusAccountStatus.NotLoggedIn);
        vm.UserName.Should().BeNull();
        vm.IsPremium.Should().BeFalse();
        vm.MaskedSavedKey.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Logout_NoKey_CannotExecute()
    {
        var (vm, _, _, _) = Make();
        vm.LogoutCommand.CanExecute(null).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  OpenNexusApiPage
    // ------------------------------------------------------------------

    [Fact]
    public void OpenNexusApiPage_CallsLauncher()
    {
        var (vm, _, _, launcher) = Make();

        vm.OpenNexusApiPageCommand.Execute(null);

        launcher.OpenedPaths.Should().HaveCount(1);
        launcher.OpenedPaths[0].Should()
            .Be("https://www.nexusmods.com/users/myaccount?tab=api");
    }

    [Fact]
    public void OpenNexusApiPage_LauncherThrows_SetsError()
    {
        var (vm, _, _, launcher) = Make();
        launcher.ExceptionToThrow = new InvalidOperationException("no browser");

        vm.OpenNexusApiPageCommand.Execute(null);

        vm.ErrorMessage.Should().Contain("no browser");
    }

    // ------------------------------------------------------------------
    //  StatusText / StatusColor
    // ------------------------------------------------------------------

    [Fact]
    public void StatusText_NotLoggedIn()
    {
        var (vm, _, _, _) = Make();
        vm.StatusText.Should().Be("Not logged in");
    }

    [Fact]
    public async Task StatusText_LoggedIn_IncludesName()
    {
        var (vm, keyProvider, validator, _) = Make();
        keyProvider.Key = "abcdef1234567890";
        validator.ResultToReturn = FakeNexusCredentialValidator.Valid(
            userName: "someuser", isPremium: true);

        await vm.RefreshAsync();

        vm.StatusText.Should().Contain("someuser");
        vm.StatusText.Should().Contain("premium: yes");
    }
}
