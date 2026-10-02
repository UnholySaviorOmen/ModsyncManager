using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Modsync.Gui.Modules.Tests.Pack;

public class PackVMTests
{
    private static (PackVM vm, FakePackRunner runner, FakeFilePickerService picker)
        Make()
    {
        var runner = new FakePackRunner();
        var picker = new FakeFilePickerService();
        var sink = new ObservableLogSink();
        var log = new LogVM(sink);

        var vm = new PackVM(
            runner, picker, log,
            NullLogger<PackVM>.Instance);

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

        vm.State.Should().Be(PackState.Configuration);
        vm.IsConfiguring.Should().BeTrue();
        vm.IsPacking.Should().BeFalse();
        vm.IsSuccess.Should().BeFalse();
        vm.IsFailure.Should().BeFalse();
        vm.Summary.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void PackCommand_NoConfig_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.PackCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PackCommand_InvalidConfig_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.ConfigPicker.SetPath(@"C:\nope\does-not-exist.json");

        vm.PackCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PackCommand_ValidConfig_CanExecute()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, _, _) = Make();
            vm.ConfigPicker.SetPath(tmp);

            vm.PackCommand.CanExecute(null).Should().BeTrue();
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
    public async Task PackAsync_Success_TransitionsToSuccess()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakePackRunner.MakeSummary(
                modsScanned: 5, filesScanned: 100);

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            vm.State.Should().Be(PackState.Success);
            vm.IsSuccess.Should().BeTrue();
            vm.Summary.Should().NotBeNull();
            vm.Summary!.ModsScanned.Should().Be(5);
            vm.Summary!.FilesScanned.Should().Be(100);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task PackAsync_PassesConfigPathToRunner()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakePackRunner.MakeSummary();

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            runner.LastConfigPath.Should().Be(tmp);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task PackAsync_DoesNotClearLog()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakePackRunner.MakeSummary();

            vm.Log.Entries.Add(new LogEntry(
                DateTimeOffset.Now, Microsoft.Extensions.Logging.LogLevel.Information,
                "old entry"));

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            // Лог не чистится автоматически (решение 3.9.4).
            // История копится, пользователь чистит вручную.
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
    public async Task PackAsync_RunnerThrows_TransitionsToFailure()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new InvalidOperationException("boom");

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            vm.State.Should().Be(PackState.Failure);
            vm.IsFailure.Should().BeTrue();
            vm.ErrorMessage.Should().Contain("boom");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task PackAsync_Cancelled_ReturnsToConfiguration()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new OperationCanceledException();

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            vm.State.Should().Be(PackState.Configuration);
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
    public async Task CancelCommand_CancelsRunningPack()
    {
        var tmp = MakeTempJson();
        try
        {
            var (vm, runner, _) = Make();
            runner.Gate = new TaskCompletionSource();

            vm.ConfigPicker.SetPath(tmp);

            var packTask = vm.PackCommand.ExecuteAsync(null);

            vm.State.Should().Be(PackState.Packing);
            vm.CancelCommand.CanExecute(null).Should().BeTrue();

            vm.CancelCommand.Execute(null);

            await packTask;

            vm.State.Should().Be(PackState.Configuration);
            vm.ErrorMessage.Should().Be("Cancelled.");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void CancelCommand_NotPacking_CannotExecute()
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
            runner.ResultToReturn = FakePackRunner.MakeSummary();

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            vm.State.Should().Be(PackState.Success);
            vm.Summary.Should().NotBeNull();

            vm.DoneCommand.Execute(null);

            vm.State.Should().Be(PackState.Configuration);
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
            runner.ResultToReturn = FakePackRunner.MakeSummary();

            vm.IsConfiguring.Should().BeTrue();
            vm.IsPacking.Should().BeFalse();

            vm.ConfigPicker.SetPath(tmp);
            await vm.PackCommand.ExecuteAsync(null);

            vm.IsConfiguring.Should().BeFalse();
            vm.IsPacking.Should().BeFalse();
            vm.IsSuccess.Should().BeTrue();
            vm.IsFailure.Should().BeFalse();
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
