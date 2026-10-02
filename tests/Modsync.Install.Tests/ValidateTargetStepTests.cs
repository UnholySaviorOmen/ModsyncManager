// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class ValidateTargetStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ValidateTargetStep _step;

    public ValidateTargetStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-vt-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _step = new ValidateTargetStep(NullLogger<ValidateTargetStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private Task<ValidateTargetStep.Output> Run(
        string instancePath,
        bool usedExplicitTarget = true)
        => _step.ExecuteAsync(new ValidateTargetStep.Input
        {
            InstancePath = instancePath,
            UsedExplicitTarget = usedExplicitTarget,
        }, CancellationToken.None);

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_NormalSubfolder_Passes()
    {
        var path = Path.Combine(_tempDir, "my-pack");

        var output = await Run(path);

        output.InstancePath.Should().Be(Path.GetFullPath(path));
        Directory.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_DeepSubfolder_Passes()
    {
        var path = Path.Combine(_tempDir, "a", "b", "c", "my-pack");

        await Run(path);

        Directory.Exists(path).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Корень диска
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DriveRoot_Fails()
    {
        var act = async () => await Run("C:\\");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*drive root*");
    }

    // ------------------------------------------------------------------
    //  Системные папки
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_WindowsFolder_Fails()
    {
        var act = async () => await Run("C:\\Windows\\SomePack");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*system folder*");
    }

    [Fact]
    public async Task Execute_ProgramFiles_Fails()
    {
        var act = async () => await Run("C:\\Program Files\\SomePack");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*system folder*");
    }

    [Fact]
    public async Task Execute_ProgramData_Fails()
    {
        var act = async () => await Run("C:\\ProgramData\\SomePack");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*system folder*");
    }

    [Fact]
    public async Task Execute_UsersRoot_Fails()
    {
        var act = async () => await Run("C:\\Users");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Users folder*");
    }

    // ------------------------------------------------------------------
    //  Папка exe
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_ExeFolderItself_Fails()
    {
        var exeDir = Path.GetFullPath(AppContext.BaseDirectory);

        var act = async () => await Run(exeDir);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*executable folder*");
    }

    [Fact]
    public async Task Execute_ParentOfExeFolder_Fails()
    {
        // Родитель папки exe: bin/Debug/net8.0 → bin/Debug
        var exeDir = Path.GetFullPath(AppContext.BaseDirectory);
        var parent = Path.GetFullPath(Path.Combine(exeDir, "..", ".."));

        var act = async () => await Run(parent);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*parent of the ModsyncManager*");
    }

    [Fact]
    public async Task Execute_ChildOfExeFolder_Passes()
    {
        // bin/Debug/net8.0/Instances/MyPack — это ок.
        var exeDir = Path.GetFullPath(AppContext.BaseDirectory);
        var child = Path.Combine(exeDir, "Instances", "MyPack");

        await Run(child);

        Directory.Exists(child).Should().BeTrue();

        // Чистим за собой.
        try { Directory.Delete(Path.Combine(exeDir, "Instances"), recursive: true); }
        catch { }
    }

    // ------------------------------------------------------------------
    //  Прочее
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            new ValidateTargetStep.Input
            {
                InstancePath = Path.Combine(_tempDir, "x"),
                UsedExplicitTarget = true,
            }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
