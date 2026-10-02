using FluentAssertions;
using Modsync.Gui.Modules.Install.ViewModels;
using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Modules.Tests.Install;

public class InstallVMTests
{
    private static (InstallVM vm, FakeInstallRunner runner, FakeFilePickerService picker)
        Make()
    {
        var runner = new FakeInstallRunner();
        var picker = new FakeFilePickerService();
        var sink = new ObservableLogSink();
        var log = new LogVM(sink);

        var vm = new InstallVM(
            runner, picker, log,
            NullLogger<InstallVM>.Instance);

        return (vm, runner, picker);
    }

    private static string MakeTempJson()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "{}");
        return tmp;
    }

    // ------------------------------------------------------------------
    //  Configuration
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_IsConfiguration()
    {
        var (vm, _, _) = Make();

        vm.State.Should().Be(InstallState.Configuration);
        vm.IsConfiguring.Should().BeTrue();
        vm.IsInstalling.Should().BeFalse();
        vm.IsSuccess.Should().BeFalse();
        vm.IsFailure.Should().BeFalse();
        vm.Summary.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void InstallCommand_NoModlist_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.InstallCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void InstallCommand_InvalidModlist_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.ModlistPicker.SetPath(@"C:\nope\does-not-exist.json");

        vm.InstallCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void InstallCommand_ValidModlist_CanExecute()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, _, _) = Make();
            vm.ModlistPicker.SetPath(tmp);

            vm.InstallCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ------------------------------------------------------------------
    //  Run — success
    // ------------------------------------------------------------------

    [Fact]
    public async Task InstallAsync_Success_TransitionsToSuccess()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary(
                modsCreated: 5, archivesPresent: 3);

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            vm.State.Should().Be(InstallState.Success);
            vm.IsSuccess.Should().BeTrue();
            vm.Summary.Should().NotBeNull();
            vm.Summary!.ModsCreated.Should().Be(5);
            vm.Summary!.ArchivesAlreadyPresent.Should().Be(3);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task InstallAsync_PassesManifestPathToRunner()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            runner.LastManifestPath.Should().Be(tmp);
            runner.LastTarget.Should().BeNull();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task InstallAsync_WithTarget_PassesTargetToRunner()
    {
        var tmp = MakeTempJson();
        var dir = Path.Combine(Path.GetTempPath(), "modsyncmanager-target-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.ModlistPicker.SetPath(tmp);
            vm.TargetPicker.SetPath(dir);
            await vm.InstallCommand.ExecuteAsync(null);

            runner.LastTarget.Should().Be(dir);
        }
        finally
        {
            File.Delete(tmp);
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task InstallAsync_WithoutTarget_PassesNullToRunner()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            runner.LastTarget.Should().BeNull();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task InstallAsync_DoesNotClearLog()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.Log.Entries.Add(new LogEntry(
                DateTimeOffset.Now, Microsoft.Extensions.Logging.LogLevel.Information,
                "old entry"));

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            // Лог не чистится автоматически (решение 3.9.4).
            vm.Log.Entries.Should().HaveCount(1);
            vm.Log.Entries[0].Message.Should().Be("old entry");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ------------------------------------------------------------------
    //  Run — failure
    // ------------------------------------------------------------------

    [Fact]
    public async Task InstallAsync_RunnerThrows_TransitionsToFailure()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new InvalidOperationException("boom");

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            vm.State.Should().Be(InstallState.Failure);
            vm.IsFailure.Should().BeTrue();
            vm.ErrorMessage.Should().Contain("boom");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task InstallAsync_Cancelled_ReturnsToConfiguration()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new OperationCanceledException();

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            vm.State.Should().Be(InstallState.Configuration);
            vm.ErrorMessage.Should().Be("Cancelled.");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ------------------------------------------------------------------
    //  Cancel
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelCommand_CancelsRunningInstall()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.Gate = new TaskCompletionSource();

            vm.ModlistPicker.SetPath(tmp);

            var installTask = vm.InstallCommand.ExecuteAsync(null);

            vm.State.Should().Be(InstallState.Installing);
            vm.CancelCommand.CanExecute(null).Should().BeTrue();

            vm.CancelCommand.Execute(null);

            await installTask;

            vm.State.Should().Be(InstallState.Configuration);
            vm.ErrorMessage.Should().Be("Cancelled.");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void CancelCommand_NotInstalling_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Done
    // ------------------------------------------------------------------

    [Fact]
    public async Task DoneCommand_ResetsState()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            vm.State.Should().Be(InstallState.Success);
            vm.Summary.Should().NotBeNull();

            vm.DoneCommand.Execute(null);

            vm.State.Should().Be(InstallState.Configuration);
            vm.Summary.Should().BeNull();
            vm.ErrorMessage.Should().BeNull();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ------------------------------------------------------------------
    //  Visibility
    // ------------------------------------------------------------------

    [Fact]
    public async Task State_TransitionsUpdateVisibilityFlags()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.IsConfiguring.Should().BeTrue();
            vm.IsInstalling.Should().BeFalse();

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);

            vm.IsConfiguring.Should().BeFalse();
            vm.IsInstalling.Should().BeFalse();
            vm.IsSuccess.Should().BeTrue();
            vm.IsFailure.Should().BeFalse();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ------------------------------------------------------------------
    //  PrepareForInstall (IInstallTarget)
    // ------------------------------------------------------------------

    [Fact]
    public void PrepareForInstall_SetsBothPickersAndResetsToConfiguration()
    {
        var tmp = MakeTempJson();
        var target = Path.Combine(Path.GetTempPath(), "modsyncmanager-target-" + Guid.NewGuid());
        Directory.CreateDirectory(target);
        try
        {
            var (vm, _, _) = Make();

            vm.ModlistPicker.Path.Should().BeNull();
            vm.TargetPicker.Path.Should().BeNull();

            vm.PrepareForInstall(tmp, target);

            vm.ModlistPicker.Path.Should().Be(tmp);
            vm.TargetPicker.Path.Should().Be(target);
            vm.State.Should().Be(InstallState.Configuration);
        }
        finally
        {
            File.Delete(tmp);
            Directory.Delete(target);
        }
    }

    [Fact]
    public async Task PrepareForInstall_FromSuccess_ResetsState()
    {
        var tmp = MakeTempJson();
        var target = Path.Combine(Path.GetTempPath(), "modsyncmanager-target-" + Guid.NewGuid());
        Directory.CreateDirectory(target);
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeInstallRunner.MakeSummary();

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);
            vm.State.Should().Be(InstallState.Success);

            vm.PrepareForInstall(tmp, target);

            vm.State.Should().Be(InstallState.Configuration);
            vm.Summary.Should().BeNull();
            vm.ErrorMessage.Should().BeNull();
        }
        finally
        {
            File.Delete(tmp);
            Directory.Delete(target);
        }
    }

    [Fact]
    public async Task PrepareForInstall_FromFailure_ResetsState()
    {
        var tmp = MakeTempJson();
        var target = Path.Combine(Path.GetTempPath(), "modsyncmanager-target-" + Guid.NewGuid());
        Directory.CreateDirectory(target);
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new InvalidOperationException("boom");

            vm.ModlistPicker.SetPath(tmp);
            await vm.InstallCommand.ExecuteAsync(null);
            vm.State.Should().Be(InstallState.Failure);

            vm.PrepareForInstall(tmp, target);

            vm.State.Should().Be(InstallState.Configuration);
            vm.ErrorMessage.Should().BeNull();
        }
        finally
        {
            File.Delete(tmp);
            Directory.Delete(target);
        }
    }

    [Fact]
    public void PrepareForInstall_EmptyManifestPath_NoOp()
    {
        var (vm, _, _) = Make();

        var act = () => vm.PrepareForInstall("", "/some/target");
        act.Should().NotThrow();

        vm.ModlistPicker.Path.Should().BeNull();
        vm.TargetPicker.Path.Should().BeNull();
    }

    [Fact]
    public void PrepareForInstall_EmptyTargetPath_NoOp()
    {
        var (vm, _, _) = Make();

        var act = () => vm.PrepareForInstall("/some/manifest.json", "");
        act.Should().NotThrow();

        vm.ModlistPicker.Path.Should().BeNull();
        vm.TargetPicker.Path.Should().BeNull();
    }
}
