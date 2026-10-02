using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Gui.Modules.Verify.ViewModels;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Modsync.Gui.Modules.Tests.Verify;

public class VerifyVMTests
{
    private static (VerifyVM vm, FakeVerifyRunner runner, FakeFilePickerService picker)
        Make()
    {
        var runner = new FakeVerifyRunner();
        var picker = new FakeFilePickerService();
        var sink = new ObservableLogSink();
        var log = new LogVM(sink);

        var vm = new VerifyVM(
            runner, picker, log,
            NullLogger<VerifyVM>.Instance);

        return (vm, runner, picker);
    }

    private static string MakeTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-verify-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ------------------------------------------------------------------
    //  Configuration
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_IsConfiguration()
    {
        var (vm, _, _) = Make();

        vm.State.Should().Be(VerifyState.Configuration);
        vm.IsConfiguring.Should().BeTrue();
        vm.IsVerifying.Should().BeFalse();
        vm.IsSuccess.Should().BeFalse();
        vm.IsFailure.Should().BeFalse();
        vm.Report.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.Rows.Should().BeEmpty();
    }

    [Fact]
    public void VerifyCommand_NoTarget_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.VerifyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void VerifyCommand_InvalidTarget_CannotExecute()
    {
        var (vm, _, _) = Make();

        vm.TargetPicker.SetPath(@"C:\nope\does-not-exist");

        vm.VerifyCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void VerifyCommand_ValidTarget_CanExecute()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, _, _) = Make();
            vm.TargetPicker.SetPath(dir);

            vm.VerifyCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  Success, IsOk
    // ------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_OkReport_TransitionsToSuccess()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.OkReport(
                targetPath: dir, passedCount: 10);

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.State.Should().Be(VerifyState.Success);
            vm.IsSuccess.Should().BeTrue();
            vm.IsOk.Should().BeTrue();
            vm.HasFailures.Should().BeFalse();
            vm.PassedCount.Should().Be(10);
            vm.FailedCount.Should().Be(0);
            vm.ResultTitle.Should().Be("All checks passed");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task VerifyAsync_OkReport_NoRowsByDefault()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.OkReport(passedCount: 5);

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            // ShowAllChecks = false → только failures (их нет) → пусто.
            vm.Rows.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task VerifyAsync_OkReport_ShowAllChecks_FillsRows()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.OkReport(passedCount: 5);

            vm.TargetPicker.SetPath(dir);
            vm.ShowAllChecks = true;
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.Rows.Should().HaveCount(5);
            vm.Rows.Should().AllSatisfy(r => r.Passed.Should().BeTrue());
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  Success, NotOk
    // ------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_FailingReport_TransitionsToSuccessWithFailures()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.FailingReport(
                targetPath: dir,
                passedCount: 5,
                ("Mod: SkyUI", "Mod directory not found"),
                ("Profile: modlist.txt", "Content differs"));

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.State.Should().Be(VerifyState.Success);
            vm.IsSuccess.Should().BeTrue();
            vm.IsOk.Should().BeFalse();
            vm.HasFailures.Should().BeTrue();
            vm.PassedCount.Should().Be(5);
            vm.FailedCount.Should().Be(2);
            vm.ResultTitle.Should().Be("2 check(s) failed");

            // По умолчанию — только failures.
            vm.Rows.Should().HaveCount(2);
            vm.Rows.Should().OnlyContain(r => !r.Passed);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task VerifyAsync_FailingReport_ShowAllChecks_IncludesPassed()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.FailingReport(
                targetPath: dir,
                passedCount: 5,
                ("Bad", "msg"));

            vm.TargetPicker.SetPath(dir);
            vm.ShowAllChecks = true;
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.Rows.Should().HaveCount(6); // 5 passed + 1 failed
            vm.Rows.Count(r => r.Passed).Should().Be(5);
            vm.Rows.Count(r => !r.Passed).Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  ShowAllChecks toggle after run
    // ------------------------------------------------------------------

    [Fact]
    public async Task ShowAllChecks_AfterRun_RebuildsRows()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.FailingReport(
                targetPath: dir,
                passedCount: 3,
                ("Bad1", "msg1"),
                ("Bad2", "msg2"));

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.Rows.Should().HaveCount(2); // only failures

            vm.ShowAllChecks = true;
            vm.Rows.Should().HaveCount(5); // 3 passed + 2 failed

            vm.ShowAllChecks = false;
            vm.Rows.Should().HaveCount(2); // back to failures
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  Failure (exception)
    // ------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_RunnerThrows_TransitionsToFailure()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new InvalidOperationException("boom");

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.State.Should().Be(VerifyState.Failure);
            vm.IsFailure.Should().BeTrue();
            vm.ErrorMessage.Should().Contain("boom");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task VerifyAsync_Cancelled_ReturnsToConfiguration()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ExceptionToThrow = new OperationCanceledException();

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.State.Should().Be(VerifyState.Configuration);
            vm.ErrorMessage.Should().Be("Cancelled.");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  Cancel command
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelCommand_CancelsRunningVerify()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.Gate = new TaskCompletionSource();

            vm.TargetPicker.SetPath(dir);

            var verifyTask = vm.VerifyCommand.ExecuteAsync(null);

            vm.State.Should().Be(VerifyState.Verifying);
            vm.CancelCommand.CanExecute(null).Should().BeTrue();

            vm.CancelCommand.Execute(null);
            await verifyTask;

            vm.State.Should().Be(VerifyState.Configuration);
            vm.ErrorMessage.Should().Be("Cancelled.");
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void CancelCommand_NotVerifying_CannotExecute()
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
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.OkReport(passedCount: 3);

            vm.TargetPicker.SetPath(dir);
            vm.ShowAllChecks = true;
            await vm.VerifyCommand.ExecuteAsync(null);

            vm.State.Should().Be(VerifyState.Success);
            vm.Report.Should().NotBeNull();
            vm.Rows.Should().HaveCount(3);

            vm.DoneCommand.Execute(null);

            vm.State.Should().Be(VerifyState.Configuration);
            vm.Report.Should().BeNull();
            vm.Rows.Should().BeEmpty();
            vm.IsOk.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  Clear on start
    // ------------------------------------------------------------------

    [Fact]
    public async Task VerifyAsync_KeepsLogClearsRows()
    {
        var dir = MakeTempDir();
        try
        {
            var (vm, runner, _) = Make();
            runner.ResultToReturn = FakeVerifyRunner.OkReport(passedCount: 1);

            vm.Log.Entries.Add(new LogEntry(
                DateTimeOffset.Now, Microsoft.Extensions.Logging.LogLevel.Information,
                "old entry"));
            vm.Rows.Add(new VerifyRowVM { Name = "old", Passed = true });

            vm.TargetPicker.SetPath(dir);
            await vm.VerifyCommand.ExecuteAsync(null);

            // Лог сохраняется (решение 3.9.4).
            vm.Log.Entries.Should().HaveCount(1);

            // Rows очищаются (VerifyVM.Rows.Clear() остался).
            vm.Rows.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    // ------------------------------------------------------------------
    //  VerifyRowVM computed props
    // ------------------------------------------------------------------

    [Fact]
    public void VerifyRowVM_PassedStatus_HasCheckmark()
    {
        var row = new VerifyRowVM { Name = "x", Passed = true };

        row.StatusGlyph.Should().Be("✓");
        row.StatusColor.Should().Be("#7fc98a");
    }

    [Fact]
    public void VerifyRowVM_FailedStatus_HasCross()
    {
        var row = new VerifyRowVM { Name = "x", Passed = false };

        row.StatusGlyph.Should().Be("×");
        row.StatusColor.Should().Be("#d97777");
    }
}
