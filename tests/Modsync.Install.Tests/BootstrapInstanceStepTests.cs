// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Modsync.Install.Steps;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Install.Tests;

public class BootstrapInstanceStepTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BootstrapInstanceStep _step;

    public BootstrapInstanceStepTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-install-bs-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _step = new BootstrapInstanceStep(
            NullLogger<BootstrapInstanceStep>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    /// <summary>
    /// Готовит «правильный» instancePath: создаёт папку и кладёт modlist.json.
    /// Это то, что сделал бы ResolveTargetStep до вызова BootstrapInstanceStep.
    /// </summary>
    private string MakePreparedInstance(string name = "Test Pack")
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "modlist.json"), "{}");
        return path;
    }

    private static BootstrapInstanceStep.Input MakeInput(string instancePath)
        => new() { InstancePath = instancePath };

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_PreparedInstance_CreatesFullStructure()
    {
        var instancePath = MakePreparedInstance();

        var output = await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        Directory.Exists(output.Mo2Path).Should().BeTrue();
        Directory.Exists(output.DownloadsPath).Should().BeTrue();
        Directory.Exists(output.ModsPath).Should().BeTrue();
        Directory.Exists(output.ProfilesPath).Should().BeTrue();
        Directory.Exists(output.PluginsPath).Should().BeTrue();
        Directory.Exists(output.ToolsPath).Should().BeTrue();
        Directory.Exists(output.StockGamePath).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ReturnsAllPaths()
    {
        var instancePath = MakePreparedInstance();
        var fullPath = Path.GetFullPath(instancePath);

        var output = await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        output.InstancePath.Should().Be(fullPath);
        output.Mo2Path.Should().Be(Path.Combine(fullPath, "MO2"));
        output.DownloadsPath.Should().Be(Path.Combine(fullPath, "MO2", "downloads"));
        output.ModsPath.Should().Be(Path.Combine(fullPath, "MO2", "mods"));
        output.ProfilesPath.Should().Be(Path.Combine(fullPath, "MO2", "profiles"));
        output.PluginsPath.Should().Be(Path.Combine(fullPath, "MO2", "plugins"));
        output.ToolsPath.Should().Be(Path.Combine(fullPath, "MO2", "tools"));
        output.StockGamePath.Should().Be(Path.Combine(fullPath, "Stock Game"));
        output.ManifestPathInInstance.Should().Be(
            Path.Combine(fullPath, "modlist.json"));
    }

    // ------------------------------------------------------------------
    //  Идемпотентность
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_Twice_SecondRunSucceeds()
    {
        var instancePath = MakePreparedInstance();

        await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);
        var second = await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        // Структура на месте.
        Directory.Exists(second.ModsPath).Should().BeTrue();
        Directory.Exists(second.StockGamePath).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_PartiallyExistingStructure_CompletesIt()
    {
        var instancePath = MakePreparedInstance();

        // Заранее создаём только часть структуры.
        Directory.CreateDirectory(Path.Combine(instancePath, "MO2", "mods"));
        Directory.CreateDirectory(Path.Combine(instancePath, "Stock Game"));

        var output = await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        // Все папки теперь есть.
        Directory.Exists(output.DownloadsPath).Should().BeTrue();
        Directory.Exists(output.ProfilesPath).Should().BeTrue();
        Directory.Exists(output.PluginsPath).Should().BeTrue();
        Directory.Exists(output.ToolsPath).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  Не трогаем mods/ (reconcile — забота SyncModsStep)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DoesNotTouchExistingModFolders()
    {
        var instancePath = MakePreparedInstance();
        var modsPath = Path.Combine(instancePath, "MO2", "mods");

        Directory.CreateDirectory(Path.Combine(modsPath, "ExistingMod"));
        File.WriteAllText(
            Path.Combine(modsPath, "ExistingMod", "marker.txt"), "keep me");

        await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        // Папка и файл не тронуты.
        File.Exists(Path.Combine(modsPath, "ExistingMod", "marker.txt"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Execute_DoesNotTouchModlistJson()
    {
        var instancePath = MakePreparedInstance();
        var manifestPath = Path.Combine(instancePath, "modlist.json");
        var originalContent = "original manifest content";
        File.WriteAllText(manifestPath, originalContent);

        await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        File.ReadAllText(manifestPath).Should().Be(originalContent);
    }

    // ------------------------------------------------------------------
    //  Не создаёт __ModsyncManager_Output/
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_DoesNotCreateModsyncOutput()
    {
        var instancePath = MakePreparedInstance();

        await _step.ExecuteAsync(MakeInput(instancePath), CancellationToken.None);

        var modsyncOutput = Path.Combine(instancePath, "__ModsyncManager_Output");
        Directory.Exists(modsyncOutput).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Контрактные проверки
    // ------------------------------------------------------------------

    [Fact]
    public async Task Execute_NonExistentInstancePath_Throws()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");

        var act = async () => await _step.ExecuteAsync(
            MakeInput(missing), CancellationToken.None);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage("*ResolveTargetStep*");
    }

    [Fact]
    public async Task Execute_InstanceWithoutManifest_Throws()
    {
        // Папка есть, modlist.json нет — контракт нарушен.
        var instancePath = Path.Combine(_tempDir, "empty-instance");
        Directory.CreateDirectory(instancePath);

        var act = async () => await _step.ExecuteAsync(
            MakeInput(instancePath), CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*modlist.json*");
    }

    [Fact]
    public async Task Execute_EmptyPath_Throws()
    {
        var act = async () => await _step.ExecuteAsync(
            MakeInput(""), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Execute_CanceledToken_Throws()
    {
        var instancePath = MakePreparedInstance();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _step.ExecuteAsync(
            MakeInput(instancePath), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
