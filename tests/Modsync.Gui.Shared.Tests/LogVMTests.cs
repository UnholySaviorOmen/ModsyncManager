using FluentAssertions;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Tests;

public class LogsVMTests
{
    private static (LogsVM vm, FakeProcessLauncher launcher) Make()
    {
        var sink = new ObservableLogSink();
        var log = new LogVM(sink);
        var launcher = new FakeProcessLauncher();

        var vm = new LogsVM(log, launcher);
        return (vm, launcher);
    }

    [Fact]
    public void Log_IsNotNull()
    {
        var (vm, _) = Make();
        vm.Log.Should().NotBeNull();
    }

    [Fact]
    public void LogsFolderPath_ContainsModsyncManagerLogs()
    {
        var (vm, _) = Make();
        vm.LogsFolderPath.Should().Contain("ModsyncManager");
        vm.LogsFolderPath.Should().EndWith("logs");
    }

    [Fact]
    public void OpenLogsFolderCommand_CallsLauncherWithLogsFolder()
    {
        var (vm, launcher) = Make();
        vm.OpenLogsFolderCommand.Execute(null);

        launcher.OpenedPaths.Should().HaveCount(1);
        launcher.OpenedPaths[0].Should().Be(vm.LogsFolderPath);
    }

    [Fact]
    public void OpenLogsFolderCommand_LauncherThrows_DoesNotThrow()
    {
        var (vm, launcher) = Make();
        launcher.ExceptionToThrow = new InvalidOperationException("no explorer");

        var act = () => vm.OpenLogsFolderCommand.Execute(null);

        act.Should().NotThrow();
    }
}
