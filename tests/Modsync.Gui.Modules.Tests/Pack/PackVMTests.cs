// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Gui.Shared.Logging;
using Modsync.Gui.Shared.Services;
using Modsync.Gui.Shared.State;
using Modsync.Gui.Shared.ViewModels;
using Modsync.Pack;
using Modsync.Pack.Models;
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
        FakePatchDialogService patchDialog,
        IServiceProvider sp)
        Make()
    {
        var runner = new FakePackRunner();
        var picker = new FakeFilePickerService();
        var sink = new ObservableLogSink();
        var log = new LogVM(sink);
        var patchDialog = new FakePatchDialogService();

        var services = new ServiceCollection();
        services.AddSingleton(new PackConfigBuilder(
            new Modsync.Core.Archives.FileHashCache(),
            NullLogger<PackConfigBuilder>.Instance));
        services.AddTransient<PackConfigVM>(sp => new PackConfigVM(
            sp.GetRequiredService<PackConfigBuilder>(),
            picker,
            NullLogger<PackConfigVM>.Instance));
        services.AddSingleton<IPatchDialogService>(patchDialog);

        var sp = services.BuildServiceProvider();

        var vm = new PackVM(
            runner, sp, picker, log,
            NullLogger<PackVM>.Instance);

        return (vm, runner, picker, patchDialog, sp);
    }

    private string WriteValidConfig()
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid() + ".json");

        var config = new Modsync.Core.Models.Pack.PackConfig
        {
            Meta = new Modsync.Core.Models.Pack.PackMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Instance = new Modsync.Core.Models.Pack.PackInstance { Path = "." },
            Mo2 = new Modsync.Core.Models.Pack.PackMo2
            {
                Version = "2.5.2",
                Profile = "Default",
                Archive = "Mod.Organizer-2.5.2.7z",
                Source = new Modsync.Core.Models.Manifest.Sources.MirrorSourceRef
                {
                    Url = "https://example.com/Mod.Organizer-2.5.2.7z",
                    Hash = new Modsync.Core.Models.Hashing.XxHash64Value(1),
                },
                Extensions = Array.Empty<string>(),
            },
            StockGame = new Modsync.Core.Models.Pack.PackStockGame
            {
                Extras = Array.Empty<string>(),
            },
            ArchiveSources = Array.Empty<Modsync.Core.Models.Pack.PackArchiveSource>(),
        };

        File.WriteAllText(
            path,
            Modsync.Core.Models.Pack.PackConfigJson.Serialize(config));

        return path;
    }

    // ------------------------------------------------------------------
    //  Configuration
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_IsConfiguration()
    {
        var (vm, _, _, _, _) = Make();

        vm.State.Should().Be(PackState.Configuration);
        vm.IsConfiguring.Should().BeTrue();
        vm.IsPacking.Should().BeFalse();
        vm.IsSuccess.Should().BeFalse();
        vm.IsFailure.Should().BeFalse();
        vm.Summary.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.IsCreatingConfig.Should().BeFalse();
        vm.ConfigVM.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Load config — открывает форму с config
    // ------------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_OpensFormWithLoadedConfig()
    {
        var (vm, _, picker, _, _) = Make();
        var configPath = WriteValidConfig();
        picker.FileToReturn = configPath;

        await vm.LoadConfigCommand.ExecuteAsync(null);

        vm.IsCreatingConfig.Should().BeTrue();
        vm.IsConfiguring.Should().BeFalse();
        vm.ConfigVM.Should().NotBeNull();
        vm.ConfigVM!.LoadedFromPath.Should().Be(configPath);
    }

    [Fact]
    public async Task LoadConfig_PickerCancelled_NoFormOpened()
    {
        var (vm, _, picker, _, _) = Make();
        picker.FileToReturn = null;

        await vm.LoadConfigCommand.ExecuteAsync(null);

        vm.IsCreatingConfig.Should().BeFalse();
        vm.ConfigVM.Should().BeNull();
    }

    [Fact]
    public async Task LoadConfig_WhileFormOpen_NoOp()
    {
        var (vm, _, picker, _, _) = Make();
        var configPath = WriteValidConfig();
        picker.FileToReturn = configPath;

        await vm.LoadConfigCommand.ExecuteAsync(null);
        var first = vm.ConfigVM;

        await vm.LoadConfigCommand.ExecuteAsync(null);

        vm.ConfigVM.Should().BeSameAs(first);
    }

    // ------------------------------------------------------------------
    //  Create config — открывает пустую форму
    // ------------------------------------------------------------------

    [Fact]
    public void CreateConfig_OpensEmptyForm()
    {
        var (vm, _, _, _, _) = Make();

        vm.CreateConfigCommand.Execute(null);

        vm.IsCreatingConfig.Should().BeTrue();
        vm.IsConfiguring.Should().BeFalse();
        vm.ConfigVM.Should().NotBeNull();
        vm.ConfigVM!.LoadedFromPath.Should().BeNull();
    }

    [Fact]
    public void CreateConfig_WhileFormOpen_KeepsSameInstance()
    {
        var (vm, _, _, _, _) = Make();

        vm.CreateConfigCommand.Execute(null);
        var first = vm.ConfigVM;

        vm.CreateConfigCommand.Execute(null);
        var second = vm.ConfigVM;

        second.Should().BeSameAs(first);
    }

    // ------------------------------------------------------------------
    //  Cancel — закрывает форму
    // ------------------------------------------------------------------

    [Fact]
    public void CancelForm_ClosesForm_ReturnsToConfiguration()
    {
        var (vm, _, _, _, _) = Make();
        vm.CreateConfigCommand.Execute(null);

        vm.ConfigVM!.CancelCommand.Execute(null);

        vm.IsCreatingConfig.Should().BeFalse();
        vm.IsConfiguring.Should().BeTrue();
        vm.ConfigVM.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Form completed → runs packer
    // ------------------------------------------------------------------

    [Fact]
    public async Task FormCompleted_ClosesForm_AndRunsPacker()
    {
        var (vm, runner, _, _, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary();
        vm.CreateConfigCommand.Execute(null);

        var input = new PackConfigBuilderInput
        {
            InstancePath = _tempDir,
            Meta = new Modsync.Core.Models.Pack.PackMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<Modsync.Core.Models.Pack.PackArchiveSource>(),
        };

        // Trigger через рефлексию: ConfigCreated — private event.
        var field = typeof(PackConfigVM)
            .GetField("ConfigCreated",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        var handler = (Action<PackConfigBuilderInput>?)field?.GetValue(vm.ConfigVM!);
        handler?.Invoke(input);

        await Task.Delay(200);

        vm.IsCreatingConfig.Should().BeFalse();
        vm.ConfigVM.Should().BeNull();
        runner.LastConfigBuilderInput.Should().NotBeNull();
        runner.LastConfigBuilderInput!.InstancePath.Should().Be(_tempDir);
    }

    // ------------------------------------------------------------------
    //  Pack — success / failure / cancel
    // ------------------------------------------------------------------

    [Fact]
    public async Task PackFromForm_Success_TransitionsToSuccess()
    {
        var (vm, runner, _, _, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary(
            modsScanned: 5, filesScanned: 100);

        await RunFormAndWaitAsync(vm);

        vm.State.Should().Be(PackState.Success);
        vm.IsSuccess.Should().BeTrue();
        vm.Summary.Should().NotBeNull();
        vm.Summary!.ModsScanned.Should().Be(5);
    }

    [Fact]
    public async Task PackFromForm_RunnerThrows_TransitionsToFailure()
    {
        var (vm, runner, _, _, _) = Make();
        runner.ExceptionToThrow = new InvalidOperationException("boom");

        await RunFormAndWaitAsync(vm);

        vm.State.Should().Be(PackState.Failure);
        vm.ErrorMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task PackFromForm_Cancelled_ReturnsToConfiguration()
    {
        var (vm, runner, _, _, _) = Make();
        runner.ExceptionToThrow = new OperationCanceledException();

        await RunFormAndWaitAsync(vm);

        vm.State.Should().Be(PackState.Configuration);
        vm.ErrorMessage.Should().Be("Cancelled.");
    }

    // ------------------------------------------------------------------
    //  Cancel running pack
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelCommand_CancelsRunningPack()
    {
        var (vm, runner, _, _, _) = Make();
        runner.Gate = new TaskCompletionSource();

        vm.CreateConfigCommand.Execute(null);
        InvokeConfigCreated(vm, MakeInput());

        // Ждём, пока State станет Packing.
        await WaitUntilAsync(() => vm.State == PackState.Packing);

        vm.CancelCommand.CanExecute(null).Should().BeTrue();
        vm.CancelCommand.Execute(null);

        await WaitUntilAsync(() => vm.State == PackState.Configuration);

        vm.ErrorMessage.Should().Be("Cancelled.");
    }

    [Fact]
    public void CancelCommand_NotPacking_CannotExecute()
    {
        var (vm, _, _, _, _) = Make();
        vm.CancelCommand.CanExecute(null).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Done
    // ------------------------------------------------------------------

    [Fact]
    public async Task DoneCommand_ResetsState()
    {
        var (vm, runner, _, _, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary();

        await RunFormAndWaitAsync(vm);
        vm.State.Should().Be(PackState.Success);

        vm.DoneCommand.Execute(null);

        vm.State.Should().Be(PackState.Configuration);
        vm.Summary.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Patch dialog
    // ------------------------------------------------------------------

    [Fact]
    public async Task PackFromForm_WithUnmatched_CallsPatchDialogService()
    {
        var (vm, runner, _, patchDialog, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary(
            unmatchedFiles: 5,
            instancePath: "/custom/instance");

        await RunFormAndWaitAsync(vm);

        patchDialog.CallCount.Should().Be(1);
        patchDialog.LastUnmatchedCount.Should().Be(5);
        patchDialog.LastInstancePath.Should().Be("/custom/instance");
    }

    [Fact]
    public async Task PackFromForm_NoUnmatched_DoesNotCallDialog()
    {
        var (vm, runner, _, patchDialog, _) = Make();
        runner.ResultToReturn = FakePackRunner.MakeSummary(unmatchedFiles: 0);

        await RunFormAndWaitAsync(vm);

        patchDialog.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PackFromForm_Failure_DoesNotCallDialog()
    {
        var (vm, runner, _, patchDialog, _) = Make();
        runner.ExceptionToThrow = new InvalidOperationException("boom");

        await RunFormAndWaitAsync(vm);

        patchDialog.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PackFromForm_Cancelled_DoesNotCallDialog()
    {
        var (vm, runner, _, patchDialog, _) = Make();
        runner.ExceptionToThrow = new OperationCanceledException();

        await RunFormAndWaitAsync(vm);

        patchDialog.CallCount.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static PackConfigBuilderInput MakeInput(string instancePath = "/test/instance")
        => new()
        {
            InstancePath = instancePath,
            Meta = new Modsync.Core.Models.Pack.PackMeta
            {
                Name = "Test",
                Version = "1.0.0",
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Profile = "Default",
            Extensions = Array.Empty<string>(),
            Extras = Array.Empty<string>(),
            ArchiveSources = Array.Empty<Modsync.Core.Models.Pack.PackArchiveSource>(),
        };

    private static void InvokeConfigCreated(PackVM vm, PackConfigBuilderInput input)
    {
        var field = typeof(PackConfigVM)
            .GetField("ConfigCreated",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);

        var handler = (Action<PackConfigBuilderInput>?)field?.GetValue(vm.ConfigVM!);
        handler?.Invoke(input);
    }

    /// <summary>
    /// Открывает форму, шлёт ConfigCreated с дефолтным input и ждёт
    /// завершения packer-а (или перехода в Success/Failure/Configuration).
    /// </summary>
    private static async Task RunFormAndWaitAsync(PackVM vm)
    {
        vm.CreateConfigCommand.Execute(null);
        InvokeConfigCreated(vm, MakeInput());

        await WaitUntilAsync(() =>
            vm.State == PackState.Success ||
            vm.State == PackState.Failure ||
            vm.State == PackState.Configuration);

        // Даём MaybeShowPatchDialogAsync завершиться.
        await Task.Delay(150);
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }

        throw new TimeoutException("Condition not met within timeout.");
    }
}
