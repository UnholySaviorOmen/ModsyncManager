using FluentAssertions;
using Modsync.Gui.Shared.Tests.Fakes;
using Modsync.Gui.Shared.ViewModels.Controls;

namespace Modsync.Gui.Shared.Tests;

public class FilePickerVMTests
{
    private static FilePickerVM Make(
        FakeFilePickerService? picker = null,
        bool mustExist = true,
        bool folder = false)
    {
        return new FilePickerVM(picker ?? new FakeFilePickerService())
        {
            MustExist = mustExist,
            Folder = folder,
        };
    }

    [Fact]
    public void InitialState_NoPath_Invalid()
    {
        var vm = Make();
        vm.Path.Should().BeNull();
        vm.IsValid.Should().BeFalse();
        vm.Error.Should().BeNull();
    }

    [Fact]
    public void SetPath_NonExistentFile_InvalidWithError()
    {
        var vm = Make();
        vm.SetPath(@"C:\does\not\exist\file.json");

        vm.IsValid.Should().BeFalse();
        vm.Error.Should().Be("File not found");
    }

    [Fact]
    public void SetPath_ExistingFile_Valid()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var vm = Make();
            vm.SetPath(tmp);

            vm.IsValid.Should().BeTrue();
            vm.Error.Should().BeNull();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void SetPath_MustExistFalse_AnyPathValid()
    {
        var vm = Make(mustExist: false);
        vm.SetPath(@"Z:\any\path\at\all.json");

        vm.IsValid.Should().BeTrue();
        vm.Error.Should().BeNull();
    }

    [Fact]
    public void SetPath_FolderMode_NonExistentFolder_InvalidWithFolderError()
    {
        var vm = Make(folder: true);
        vm.SetPath(@"C:\does\not\exist\folder");

        vm.IsValid.Should().BeFalse();
        vm.Error.Should().Be("Folder not found");
    }

    [Fact]
    public void SetPath_FolderMode_ExistingFolder_Valid()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modsyncmanager-fp-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var vm = Make(folder: true);
            vm.SetPath(dir);

            vm.IsValid.Should().BeTrue();
            vm.Error.Should().BeNull();
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void Clear_ResetsState()
    {
        var vm = Make(mustExist: false);
        vm.SetPath(@"C:\some\path.json");
        vm.Clear();

        vm.Path.Should().BeNull();
        vm.IsValid.Should().BeFalse();
        vm.Error.Should().BeNull();
    }

    [Fact]
    public void SetPath_EmptyString_InvalidNoError()
    {
        var vm = Make();
        vm.SetPath("");

        vm.IsValid.Should().BeFalse();
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task PickAsync_FileMode_SetsPathFromService()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var picker = new FakeFilePickerService { FileToReturn = tmp };
            var vm = Make(picker);

            await vm.PickCommand.ExecuteAsync(null);

            picker.FilePickCalls.Should().Be(1);
            vm.Path.Should().Be(tmp);
            vm.IsValid.Should().BeTrue();
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public async Task PickAsync_FileMode_Cancelled_PathUnchanged()
    {
        var picker = new FakeFilePickerService { FileToReturn = null };
        var vm = Make(picker, mustExist: false);

        await vm.PickCommand.ExecuteAsync(null);

        picker.FilePickCalls.Should().Be(1);
        vm.Path.Should().BeNull();
    }

    [Fact]
    public async Task PickAsync_FolderMode_CallsFolderPicker()
    {
        var dir = Path.Combine(Path.GetTempPath(), "modsyncmanager-fp-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var picker = new FakeFilePickerService { FolderToReturn = dir };
            var vm = Make(picker, folder: true);

            await vm.PickCommand.ExecuteAsync(null);

            picker.FolderPickCalls.Should().Be(1);
            picker.FilePickCalls.Should().Be(0);
            vm.Path.Should().Be(dir);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }
}
