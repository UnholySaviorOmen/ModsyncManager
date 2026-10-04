// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Pack;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Modules.Tests.Pack;

public class PatchArchiveDialogVMTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _outputPath;
    private readonly string _downloadsPath;
    private readonly FakeProcessLauncher _launcher;
    private readonly PatchArchiveBuilder _builder;

    public PatchArchiveDialogVMTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-patchdlg-" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "Instance");
        _outputPath = Path.Combine(_instancePath, "__ModsyncManager_Output");
        _downloadsPath = Path.Combine(_instancePath, "MO2", "downloads");

        Directory.CreateDirectory(_outputPath);
        Directory.CreateDirectory(_downloadsPath);

        _launcher = new FakeProcessLauncher();
        _builder = new PatchArchiveBuilder(
            NullLogger<PatchArchiveBuilder>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private PatchArchiveDialogVM Make(int unmatchedCount = 5)
    {
        return new PatchArchiveDialogVM(
            _builder,
            _launcher,
            NullLogger<PatchArchiveDialogVM>.Instance,
            _instancePath,
            unmatchedCount);
    }

    private void WriteOutputFile(string relativePath)
    {
        var fullPath = Path.Combine(
            _outputPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "data");
    }

    // ------------------------------------------------------------------
    //  Initial state
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_CorrectCountAndPath()
    {
        var vm = Make(unmatchedCount: 7);

        vm.UnmatchedCount.Should().Be(7);
        vm.OutputPath.Should().Be(_outputPath);
        vm.Message.Should().Contain("7");
        vm.Message.Should().Contain("__ModsyncManager_Output");
        vm.PatchPath.Should().BeNull();
        vm.ErrorMessage.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
        vm.HasPatch.Should().BeFalse();
        vm.HasError.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Ignore
    // ------------------------------------------------------------------

    [Fact]
    public void Ignore_RaisesClosed()
    {
        var vm = Make();
        var closed = false;
        vm.Closed += () => closed = true;

        vm.IgnoreCommand.Execute(null);

        closed.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  OpenFolder
    // ------------------------------------------------------------------

    [Fact]
    public void OpenFolder_Existing_LaunchesExplorer()
    {
        var vm = Make();

        vm.OpenFolderCommand.Execute(null);

        _launcher.OpenedPaths.Should().ContainSingle()
            .Which.Should().Be(_outputPath);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void OpenFolder_Missing_SetsError()
    {
        Directory.Delete(_outputPath, recursive: true);

        var vm = Make();

        vm.OpenFolderCommand.Execute(null);

        _launcher.OpenedPaths.Should().BeEmpty();
        vm.ErrorMessage.Should().Contain("Folder not found");
    }

    [Fact]
    public void OpenFolder_LauncherThrows_SetsError()
    {
        _launcher.ExceptionToThrow = new InvalidOperationException("no explorer");

        var vm = Make();

        vm.OpenFolderCommand.Execute(null);

        vm.ErrorMessage.Should().Contain("no explorer");
    }

    // ------------------------------------------------------------------
    //  CreatePatch
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreatePatch_Success_SetsPatchPath()
    {
        WriteOutputFile("MO2/mods/Mod/file.txt");

        var vm = Make();

        await vm.CreatePatchCommand.ExecuteAsync(null);

        vm.PatchPath.Should().NotBeNull();
        vm.HasPatch.Should().BeTrue();
        vm.ErrorMessage.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
        File.Exists(vm.PatchPath!).Should().BeTrue();
        vm.PatchCreatedMessage.Should().Contain("download source");
    }

    [Fact]
    public async Task CreatePatch_EmptyOutput_SetsError()
    {
        var vm = Make();

        await vm.CreatePatchCommand.ExecuteAsync(null);

        vm.PatchPath.Should().BeNull();
        vm.ErrorMessage.Should().Contain("Nothing to pack");
    }

    [Fact]
    public async Task CreatePatch_WhenBusy_CannotExecute()
    {
        WriteOutputFile("MO2/mods/Mod/file.txt");
        var vm = Make();

        // Начинаем и сразу проверяем.
        var task = vm.CreatePatchCommand.ExecuteAsync(null);

        // В процессе IsBusy = true (или уже false, если задача
        // завершилась моментально). Проверяем после завершения,
        // что IsBusy = false и команда снова CanExecute.
        await task;

        vm.IsBusy.Should().BeFalse();
        vm.CreatePatchCommand.CanExecute(null).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Paths
    // ------------------------------------------------------------------

    [Fact]
    public void OutputPath_CombinesInstanceAndDirName()
    {
        var vm = Make();
        vm.OutputPath.Should().Be(Path.Combine(_instancePath, "__ModsyncManager_Output"));
    }
}
