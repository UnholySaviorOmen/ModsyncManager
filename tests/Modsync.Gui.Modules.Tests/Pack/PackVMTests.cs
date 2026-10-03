using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Pack;
using Modsync.Pack.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Modsync.Gui.Modules.Tests.Pack;

public class PackVMTests : IDisposable
{
    private readonly string _tempDir;

    public PackVMTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-packvm-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private (
        PackVM vm,
        FakePackRunner runner,
        FakeFilePickerService picker,
        IServiceProvider sp)
        Make()
    {
        var runner = new FakePackRunner();
        var picker = new FakeFilePickerService();
        var sink = new ObservableLogSink();
        var log = new LogVM(sink);

        var services = new ServiceCollection();
        services.AddSingleton(new Modsync.Pack.PackConfigBuilder(
            new Modsync.Core.Archives.FileHashCache(),
            NullLogger<Modsync.Pack.PackConfigBuilder>.Instance));
        services.AddTransient<CreatePackConfigVM>(sp => new CreatePackConfigVM(
            sp.GetRequiredService<Modsync.Pack.PackConfigBuilder>(),
            picker,
            NullLogger<CreatePackConfigVM>.Instance));

        var sp = services.BuildServiceProvider();

        var vm = new PackVM(
            runner, sp, picker, log,
            NullLogger<PackVM>.Instance);

        return (vm, runner, picker, sp);
    }

    private string MakeTempJson()
    {
        var tmp = Path.Combine(_tempDir, Guid.NewGuid() + ".json");
        File.WriteAllText(tmp, "{}");
        return tmp;
    }

    // ------------------------------------------------------------------
    //  Configuration
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_IsConfiguration()
    {
        var (vm, _, _, _) = Make();

        vm.State.Should().Be(PackState.Configuration);
        vm.IsConfiguring.Should().BeTrue();
        vm.IsPacking.Should().BeFalse();
        vm.IsSuccess.Should().BeFalse();
        vm.IsFailure.Should().BeFalse();
        vm.Summary.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.IsCreatingConfig.Should().BeFalse();
        vm.CreateConfigVM.Should().BeNull();
    }

    [Fact]
    public void PackCommand_NoConfig_CannotExecute()
    {
        var (vm, _, _, _) = Make();

        vm.PackCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void PackCommand_ValidConfig_CanExecute()
    {
        var (vm, _, _, _) = Make();
        vm.ConfigPicker.SetPath(MakeTempJson());

        vm.PackCommand.CanExecute(null).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Pack
    // ------------------------------------------------------------------

    [Fact]
    public async Task PackAsync_Success_TransitionsToSuccess()
    {
        var (vm, runner, _, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary(
            modsScanned: 5, filesScanned: 100);

        vm.ConfigPicker.SetPath(MakeTempJson());
        await vm.PackCommand.ExecuteAsync(null);

        vm.State.Should().Be(PackState.Success);
        vm.Summary!.ModsScanned.Should().Be(5);
    }

    [Fact]
    public async Task PackAsync_RunnerThrows_TransitionsToFailure()
    {
        var (vm, runner, _, _) = Make();
        runner.ExceptionToThrow = new InvalidOperationException("boom");

        vm.ConfigPicker.SetPath(MakeTempJson());
        await vm.PackCommand.ExecuteAsync(null);

        vm.State.Should().Be(PackState.Failure);
        vm.ErrorMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task PackAsync_Cancelled_ReturnsToConfiguration()
    {
        var (vm, runner, _, _) = Make();
        runner.ExceptionToThrow = new OperationCanceledException();

        vm.ConfigPicker.SetPath(MakeTempJson());
        await vm.PackCommand.ExecuteAsync(null);

        vm.State.Should().Be(PackState.Configuration);
        vm.ErrorMessage.Should().Be("Cancelled.");
    }

    // ------------------------------------------------------------------
    //  Create Config — open / close
    // ------------------------------------------------------------------

    [Fact]
    public void OpenCreateConfig_CreatesVM_AndHidesConfiguration()
    {
        var (vm, _, _, _) = Make();

        vm.OpenCreateConfigCommand.Execute(null);

        vm.IsCreatingConfig.Should().BeTrue();
        vm.IsConfiguring.Should().BeFalse();
        vm.CreateConfigVM.Should().NotBeNull();
    }

    [Fact]
    public void OpenCreateConfig_Twice_KeepsSameInstance()
    {
        var (vm, _, _, _) = Make();

        vm.OpenCreateConfigCommand.Execute(null);
        var first = vm.CreateConfigVM;

        vm.OpenCreateConfigCommand.Execute(null);
        var second = vm.CreateConfigVM;

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void CreateConfigCancel_ClosesForm()
    {
        var (vm, _, _, _) = Make();
        vm.OpenCreateConfigCommand.Execute(null);

        vm.CreateConfigVM!.CancelCommand.Execute(null);

        vm.IsCreatingConfig.Should().BeFalse();
        vm.IsConfiguring.Should().BeTrue();
        vm.CreateConfigVM.Should().BeNull();
    }

    [Fact]
    public void PackCommand_WhileCreateConfigOpen_CannotExecute()
    {
        var (vm, _, _, _) = Make();
        vm.ConfigPicker.SetPath(MakeTempJson());

        vm.OpenCreateConfigCommand.Execute(null);

        vm.PackCommand.CanExecute(null).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Create Config — completed → runs packer
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateConfigCompleted_ClosesForm_AndRunsPacker()
    {
        var (vm, runner, _, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary();

        vm.OpenCreateConfigCommand.Execute(null);
        var configVM = vm.CreateConfigVM!;

        var input = new PackConfigBuilderInput
        {
            InstancePath = _tempDir,
            Meta = new Modsync.Core.Models.Pack.PackMeta
            {
                Name = "Test",
                Version = "0.1.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<Modsync.Core.Models.Pack.PackArchiveSource>(),
        };

        // Триггерим событие через рефлексию.
        var field = typeof(CreatePackConfigVM)
            .GetField("ConfigCreated",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        var handler = (Action<PackConfigBuilderInput>?)field?.GetValue(configVM);
        handler?.Invoke(input);

        // Fire-and-forget — ждём немного.
        await Task.Delay(200);

        vm.IsCreatingConfig.Should().BeFalse();
        vm.CreateConfigVM.Should().BeNull();
        runner.LastConfigBuilderInput.Should().NotBeNull();
        runner.LastConfigBuilderInput!.InstancePath.Should().Be(_tempDir);
    }

    // ------------------------------------------------------------------
    //  Cancel
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelCommand_CancelsRunningPack()
    {
        var (vm, runner, _, _) = Make();
        runner.Gate = new TaskCompletionSource();

        vm.ConfigPicker.SetPath(MakeTempJson());

        var packTask = vm.PackCommand.ExecuteAsync(null);

        vm.State.Should().Be(PackState.Packing);
        vm.CancelCommand.CanExecute(null).Should().BeTrue();

        vm.CancelCommand.Execute(null);
        await packTask;

        vm.State.Should().Be(PackState.Configuration);
        vm.ErrorMessage.Should().Be("Cancelled.");
    }

    // ------------------------------------------------------------------
    //  Done
    // ------------------------------------------------------------------

    [Fact]
    public async Task DoneCommand_ResetsState()
    {
        var (vm, runner, _, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary();

        vm.ConfigPicker.SetPath(MakeTempJson());
        await vm.PackCommand.ExecuteAsync(null);

        vm.DoneCommand.Execute(null);

        vm.State.Should().Be(PackState.Configuration);
        vm.Summary.Should().BeNull();
    }
}
