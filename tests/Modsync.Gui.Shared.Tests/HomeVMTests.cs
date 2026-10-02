using FluentAssertions;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class HomeVMTests
{
    private static (
        HomeVM vm,
        FakeInstalledPackScanner scanner,
        FakeFilePickerService picker,
        FakeProcessLauncher launcher)
        Make()
    {
        var scanner = new FakeInstalledPackScanner();
        var picker = new FakeFilePickerService();
        var launcher = new FakeProcessLauncher();

        var vm = new HomeVM(
            scanner,
            picker,
            launcher,
            NullLoggerFactory.Instance,
            NullLogger<HomeVM>.Instance);

        return (vm, scanner, picker, launcher);
    }

    // ------------------------------------------------------------------
    //  Refresh
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_EmptyItems()
    {
        var (vm, _, _, _) = Make();
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_NoPacks_LeavesEmpty()
    {
        var (vm, _, _, _) = Make();
        vm.Refresh();
        vm.Items.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_OnePack_AddsOneItem()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "My Pack"));

        vm.Refresh();

        vm.Items.Should().HaveCount(1);
        vm.Items[0].Name.Should().Be("My Pack");
    }

    [Fact]
    public void Refresh_MultiplePacks_AddsAll()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "A"));
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "B"));
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "C"));

        vm.Refresh();

        vm.Items.Should().HaveCount(3);
        vm.Items.Select(i => i.Name).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Refresh_CalledTwice_ReplacesItems_NotAppends()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "First"));

        vm.Refresh();
        vm.Items.Should().HaveCount(1);

        scanner.Packs.Clear();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "Second"));
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(name: "Third"));

        vm.Refresh();

        vm.Items.Should().HaveCount(2);
        vm.Items.Select(i => i.Name).Should().Equal("Second", "Third");
    }

    // ------------------------------------------------------------------
    //  Маппинг полей
    // ------------------------------------------------------------------

    [Fact]
    public void Refresh_MapsAllFields()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
            name: "My Pack",
            version: "2.5.1",
            game: "skyrimspecialedition",
            gameVersion: "1.6.1170",
            instancePath: "/custom/path",
            manifestPath: "/custom/path/modlist.json"));

        vm.Refresh();

        var item = vm.Items[0];
        item.Name.Should().Be("My Pack");
        item.Version.Should().Be("2.5.1");
        item.Game.Should().Be("skyrimspecialedition");
        item.GameVersion.Should().Be("1.6.1170");
        item.InstancePath.Should().Be("/custom/path");
        item.ManifestPath.Should().Be("/custom/path/modlist.json");
    }

    [Fact]
    public void VersionText_HasVPrefix()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(version: "1.2.3"));

        vm.Refresh();

        vm.Items[0].VersionText.Should().Be("v1.2.3");
    }

    [Fact]
    public void GameText_CombinesGameAndVersion()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
            game: "skyrimspecialedition", gameVersion: "1.6.1170"));

        vm.Refresh();

        vm.Items[0].GameText.Should().Be(
            "skyrimspecialedition · 1.6.1170");
    }

    // ------------------------------------------------------------------
    //  InstallRequested
    // ------------------------------------------------------------------

    [Fact]
    public void InstallCommand_RaisesInstallRequested_WithManifestAndTarget()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
            instancePath: "/custom/Instances/MyPack",
            manifestPath: "/custom/Instances/MyPack/modlist.json"));

        vm.Refresh();

        (string, string)? received = null;
        vm.InstallRequested += (m, t) => received = (m, t);

        vm.Items[0].InstallCommand.Execute(null);

        received.Should().NotBeNull();
        received!.Value.Item1.Should().Be("/custom/Instances/MyPack/modlist.json");
        received.Value.Item2.Should().Be("/custom/Instances/MyPack");
    }

    [Fact]
    public void InstallCommand_NoSubscribers_DoesNotThrow()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack());

        vm.Refresh();

        var act = () => vm.Items[0].InstallCommand.Execute(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleSubscribers_AllNotified()
    {
        var (vm, scanner, _, _) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
            instancePath: "/x", manifestPath: "/x/modlist.json"));

        vm.Refresh();

        var calls = new List<string>();
        vm.InstallRequested += (m, t) => calls.Add($"first:{m}|{t}");
        vm.InstallRequested += (m, t) => calls.Add($"second:{m}|{t}");

        vm.Items[0].InstallCommand.Execute(null);

        calls.Should().Equal(
            "first:/x/modlist.json|/x",
            "second:/x/modlist.json|/x");
    }

    // ------------------------------------------------------------------
    //  OpenMo2Command
    // ------------------------------------------------------------------

    [Fact]
    public void OpenMo2_FileMissing_SetsWarningMessage()
    {
        var (vm, scanner, _, launcher) = Make();
        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
            instancePath: "/no/such/instance"));

        vm.Refresh();

        var item = vm.Items[0];
        item.OpenMo2Command.Execute(null);

        item.WarningMessage.Should().Contain("not found");
        launcher.OpenedPaths.Should().BeEmpty();
    }

    [Fact]
    public void OpenMo2_FileExists_CallsLauncher()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-openmo2-" + Guid.NewGuid());
        var mo2Dir = Path.Combine(tempDir, "MO2");
        Directory.CreateDirectory(mo2Dir);
        var mo2Exe = Path.Combine(mo2Dir, "ModOrganizer.exe");
        File.WriteAllText(mo2Exe, "fake exe");

        try
        {
            var (vm, scanner, _, launcher) = Make();
            scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
                instancePath: tempDir,
                manifestPath: Path.Combine(tempDir, "modlist.json")));

            vm.Refresh();

            var item = vm.Items[0];
            item.OpenMo2Command.Execute(null);

            item.WarningMessage.Should().BeNull();
            launcher.OpenedPaths.Should().HaveCount(1);
            launcher.OpenedPaths[0].Should().Be(mo2Exe);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OpenMo2_LauncherThrows_SetsWarningMessage()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-openmo2-" + Guid.NewGuid());
        var mo2Dir = Path.Combine(tempDir, "MO2");
        Directory.CreateDirectory(mo2Dir);
        File.WriteAllText(
            Path.Combine(mo2Dir, "ModOrganizer.exe"), "fake exe");

        try
        {
            var (vm, scanner, _, launcher) = Make();
            launcher.ExceptionToThrow = new InvalidOperationException("nope");

            scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
                instancePath: tempDir,
                manifestPath: Path.Combine(tempDir, "modlist.json")));

            vm.Refresh();

            var item = vm.Items[0];
            item.OpenMo2Command.Execute(null);

            item.WarningMessage.Should().Contain("nope");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void OpenMo2_Success_ClearsPreviousWarning()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-openmo2-" + Guid.NewGuid());
        var mo2Dir = Path.Combine(tempDir, "MO2");
        Directory.CreateDirectory(mo2Dir);
        File.WriteAllText(
            Path.Combine(mo2Dir, "ModOrganizer.exe"), "fake exe");

        try
        {
            var (vm, scanner, _, _) = Make();
            scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
                instancePath: tempDir,
                manifestPath: Path.Combine(tempDir, "modlist.json")));

            vm.Refresh();

            var item = vm.Items[0];
            item.WarningMessage = "old warning";

            item.OpenMo2Command.Execute(null);

            item.WarningMessage.Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ------------------------------------------------------------------
    //  UpdateCommand
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_UserPicksFile_RaisesInstallRequested()
    {
        var (vm, scanner, picker, _) = Make();
        picker.FileToReturn = "/downloads/new-modlist.json";

        scanner.Packs.Add(FakeInstalledPackScanner.MakePack(
            instancePath: "/instances/MyPack",
            manifestPath: "/instances/MyPack/modlist.json"));

        vm.Refresh();

        (string, string)? received = null;
        vm.InstallRequested += (m, t) => received = (m, t);

        await vm.Items[0].UpdateCommand.ExecuteAsync(null);

        received.Should().NotBeNull();
        received!.Value.Item1.Should().Be("/downloads/new-modlist.json");
        received.Value.Item2.Should().Be("/instances/MyPack");
    }

    [Fact]
    public async Task Update_UserCancels_DoesNotRaise()
    {
        var (vm, scanner, picker, _) = Make();
        picker.FileToReturn = null;

        scanner.Packs.Add(FakeInstalledPackScanner.MakePack());

        vm.Refresh();

        var raised = false;
        vm.InstallRequested += (_, _) => raised = true;

        await vm.Items[0].UpdateCommand.ExecuteAsync(null);

        raised.Should().BeFalse();
    }

    [Fact]
    public async Task Update_ClearsWarningMessage()
    {
        var (vm, scanner, picker, _) = Make();
        picker.FileToReturn = null;

        scanner.Packs.Add(FakeInstalledPackScanner.MakePack());

        vm.Refresh();

        var item = vm.Items[0];
        item.WarningMessage = "old warning";

        await item.UpdateCommand.ExecuteAsync(null);

        item.WarningMessage.Should().BeNull();
    }
}
