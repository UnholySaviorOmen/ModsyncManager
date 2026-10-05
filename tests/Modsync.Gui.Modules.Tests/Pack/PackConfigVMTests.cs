// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Modsync.Core.Archives;
using Modsync.Core.Models.Pack;
using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Tests.Fakes;
using Modsync.Gui.Shared.Services;
using Modsync.Pack;
using Modsync.Pack.Models;

namespace Modsync.Gui.Modules.Tests.Pack;

public class PackConfigVMTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _instancePath;
    private readonly string _downloadsPath;
    private readonly string _profilesPath;
    private readonly FileHashCache _hashCache = new();
    private readonly PackConfigBuilder _builder;

    public PackConfigVMTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-packconfigvm-" + Guid.NewGuid());
        _instancePath = Path.Combine(_tempDir, "TestInstance");
        _downloadsPath = Path.Combine(_instancePath, "MO2", "downloads");
        _profilesPath = Path.Combine(_instancePath, "MO2", "profiles");

        Directory.CreateDirectory(_downloadsPath);
        Directory.CreateDirectory(_profilesPath);

        _builder = new PackConfigBuilder(
            _hashCache,
            NullLogger<PackConfigBuilder>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------
    //  Хелперы
    // ------------------------------------------------------------------

    private PackConfigVM Make(FakeFilePickerService? picker = null)
    {
        return new PackConfigVM(
            _builder,
            picker ?? new FakeFilePickerService(),
            NullLogger<PackConfigVM>.Instance);
    }

    private void WriteProfile(string name)
    {
        Directory.CreateDirectory(Path.Combine(_profilesPath, name));
    }

    private void WriteArchive(string name, string content = "fake")
    {
        File.WriteAllText(Path.Combine(_downloadsPath, name), content);
    }

    private string WriteConfigFile(string name, PackConfig config)
    {
        var path = Path.Combine(_instancePath, name);
        File.WriteAllText(path, PackConfigJson.Serialize(config));
        return path;
    }

    private PackConfig MakeConfig(
        string name = "Test Pack",
        string version = "1.0.0",
        string instancePath = ".",
        IReadOnlyList<PackArchiveSource>? archiveSources = null)
    {
        return new PackConfig
        {
            Meta = new PackMeta
            {
                Name = name,
                Version = version,
                Author = "tester",
                Game = "skyrimspecialedition",
                GameVersion = "1.6.1170",
            },
            Instance = new PackInstance { Path = instancePath },
            Mo2 = new PackMo2
            {
                Version = PackConfigBuilder.Mo2Version,
                Profile = "Default",
                Archive = PackConfigBuilder.Mo2ArchiveName,
                Source = new Modsync.Core.Models.Manifest.Sources.MirrorSourceRef
                {
                    Url = PackConfigBuilder.Mo2Url,
                    Hash = PackConfigBuilder.Mo2Hash,
                },
                Extensions = Array.Empty<string>(),
            },
            StockGame = new PackStockGame { Extras = Array.Empty<string>() },
            ArchiveSources = archiveSources ?? Array.Empty<PackArchiveSource>(),
        };
    }

    // ------------------------------------------------------------------
    //  Пустые дефолты
    // ------------------------------------------------------------------

    [Fact]
    public void InitialState_EmptyMetaFields()
    {
        var vm = Make();

        vm.MetaName.Should().Be("");
        vm.MetaVersion.Should().Be("");
        vm.MetaAuthor.Should().Be("");
        vm.SelectedGame.Should().Be("");
        vm.GameVersion.Should().Be("");
        vm.InstancePath.Should().BeNull();
        vm.LoadedFromPath.Should().BeNull();
        vm.HasLoadedFrom.Should().BeFalse();
    }

    [Fact]
    public void InitialState_PackCannotExecute()
    {
        var vm = Make();

        vm.PackCommand.CanExecute(null).Should().BeFalse();
        vm.SaveAsCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void InitialState_SavePathDisplay_Placeholder()
    {
        var vm = Make();

        vm.SavePathDisplay.Should().Contain("modsyncmanager-pack.json");
        vm.SavePathDisplay.Should().Contain("<instance folder>");
    }

    // ------------------------------------------------------------------
    //  Instance scan
    // ------------------------------------------------------------------

    [Fact]
    public async Task BrowseInstance_ScansProfilesAndArchives()
    {
        WriteProfile("Default");
        WriteProfile("NordicUI");
        WriteArchive("SomeMod.7z");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.InstancePath.Should().Be(_instancePath);
        vm.AvailableProfiles.Should().BeEquivalentTo(new[] { "Default", "NordicUI" });
        vm.SelectedProfile.Should().Be("Default");
        vm.UnresolvedArchives.Should().HaveCount(1);
        vm.UnresolvedArchives[0].FileName.Should().Be("SomeMod.7z");
        vm.HasUnresolvedArchives.Should().BeTrue();
    }

    [Fact]
    public async Task BrowseInstance_NoProfiles_StillWorks()
    {
        Directory.Delete(_profilesPath, recursive: true);

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.InstancePath.Should().Be(_instancePath);
        vm.AvailableProfiles.Should().BeEmpty();
        vm.HasProfiles.Should().BeFalse();
        vm.SelectedProfile.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  CanPack
    // ------------------------------------------------------------------

    [Fact]
    public async Task CanPack_AllFieldsFilled_True()
    {
        WriteProfile("Default");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.MetaName = "My Pack";
        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        vm.PackCommand.CanExecute(null).Should().BeTrue();
        vm.SaveAsCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task CanPack_MissingName_False()
    {
        WriteProfile("Default");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        vm.PackCommand.CanExecute(null).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Pack — сохранение в дефолт
    // ------------------------------------------------------------------

    [Fact]
    public async Task Pack_SavesToInstanceRoot_AndRaisesConfigCreated()
    {
        WriteProfile("Default");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.MetaName = "My Pack";
        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        PackConfigBuilderInput? captured = null;
        vm.ConfigCreated += input => captured = input;

        await vm.PackCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.InstancePath.Should().Be(_instancePath);
        captured.Meta.Name.Should().Be("My Pack");

        var savedPath = Path.Combine(_instancePath, "modsyncmanager-pack.json");
        File.Exists(savedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Pack_SaveFails_RaisesNoEvent()
    {
        WriteProfile("Default");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.MetaName = "My Pack";
        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        // Ломаем путь: делаем так, чтобы Path.Combine дал директорию вместо файла.
        Directory.CreateDirectory(Path.Combine(_instancePath, "modsyncmanager-pack.json"));

        var raised = false;
        vm.ConfigCreated += _ => raised = true;

        await vm.PackCommand.ExecuteAsync(null);

        raised.Should().BeFalse();
        vm.ErrorMessage.Should().NotBeNull();
    }

    // ------------------------------------------------------------------
    //  Save as…
    // ------------------------------------------------------------------

    [Fact]
    public async Task SaveAs_WritesToPickedPath_NoEvent()
    {
        WriteProfile("Default");

        var target = Path.Combine(_tempDir, "saved-config.json");
        var picker = new FakeFilePickerService
        {
            FolderToReturn = _instancePath,
            SaveFileToReturn = target,
        };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.MetaName = "My Pack";
        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        var raised = false;
        vm.ConfigCreated += _ => raised = true;

        await vm.SaveAsCommand.ExecuteAsync(null);

        raised.Should().BeFalse();
        File.Exists(target).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAs_PickerCancelled_NoFileWritten()
    {
        WriteProfile("Default");

        var picker = new FakeFilePickerService
        {
            FolderToReturn = _instancePath,
            SaveFileToReturn = null,
        };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.MetaName = "My Pack";
        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        var act = async () => await vm.SaveAsCommand.ExecuteAsync(null);
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    //  Load config
    // ------------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_FillsFieldsFromFile()
    {
        WriteProfile("Default");

        var config = MakeConfig(name: "Loaded Pack", version: "2.0.0");
        var configPath = WriteConfigFile("my-config.json", config);

        var picker = new FakeFilePickerService { FileToReturn = configPath };
        var vm = Make(picker);

        await vm.LoadConfigFileCommand.ExecuteAsync(null);

        vm.MetaName.Should().Be("Loaded Pack");
        vm.MetaVersion.Should().Be("2.0.0");
        vm.MetaAuthor.Should().Be("tester");
        vm.SelectedGame.Should().Be("skyrimspecialedition");
        vm.GameVersion.Should().Be("1.6.1170");
        vm.LoadedFromPath.Should().Be(configPath);
        vm.HasLoadedFrom.Should().BeTrue();
        vm.InstancePath.Should().Be(_instancePath);
    }

    [Fact]
    public async Task LoadConfig_ThenPack_SavesBackToSamePath()
    {
        WriteProfile("Default");

        var config = MakeConfig(name: "Loaded Pack");
        var configPath = WriteConfigFile("my-config.json", config);

        var picker = new FakeFilePickerService { FileToReturn = configPath };
        var vm = Make(picker);

        await vm.LoadConfigFileCommand.ExecuteAsync(null);

        vm.MetaName = "Edited Pack";
        await vm.PackCommand.ExecuteAsync(null);

        var json = File.ReadAllText(configPath);
        json.Should().Contain("Edited Pack");

        // Файл рядом с инстансом не создан.
        var defaultPath = Path.Combine(_instancePath, "modsyncmanager-pack.json");
        File.Exists(defaultPath).Should().BeFalse();
    }

    [Fact]
    public async Task LoadConfig_Malformed_SetsError()
    {
        var badPath = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(badPath, "{ not valid }");

        var picker = new FakeFilePickerService { FileToReturn = badPath };
        var vm = Make(picker);

        await vm.LoadConfigFileCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().NotBeNull();
        vm.ErrorMessage.Should().Contain("Failed to load config");
    }

    [Fact]
    public async Task LoadConfig_PickerCancelled_NoOp()
    {
        var picker = new FakeFilePickerService { FileToReturn = null };
        var vm = Make(picker);

        var act = async () => await vm.LoadConfigFileCommand.ExecuteAsync(null);
        await act.Should().NotThrowAsync();

        vm.LoadedFromPath.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Archive sources
    // ------------------------------------------------------------------

    [Fact]
    public async Task LoadConfig_ArchiveSourcesForExistingArchive_FillsSources()
    {
        WriteProfile("Default");
        WriteArchive("SomeMod.7z", "content");

        var archiveSource = new PackArchiveSource
        {
            Archive = "SomeMod.7z",
            Sources = new Modsync.Core.Models.Manifest.Sources.ArchiveSourceRef[]
            {
                new Modsync.Core.Models.Manifest.Sources.MirrorSourceRef
                {
                    Url = "https://example.com/SomeMod.7z",
                    Hash = new Modsync.Core.Models.Hashing.XxHash64Value(0x1234),
                },
            },
        };

        var config = MakeConfig(archiveSources: new[] { archiveSource });
        var configPath = WriteConfigFile("with-sources.json", config);

        var picker = new FakeFilePickerService { FileToReturn = configPath };
        var vm = Make(picker);

        await vm.LoadConfigFileCommand.ExecuteAsync(null);

        vm.UnresolvedArchives.Should().HaveCount(1);
        var row = vm.UnresolvedArchives[0];
        row.FileName.Should().Be("SomeMod.7z");
        row.Sources.Should().HaveCount(1);
        row.Sources[0].Url.Should().Be("https://example.com/SomeMod.7z");
    }

    [Fact]
    public async Task LoadConfig_OrphanedSource_PreservedInInput()
    {
        WriteProfile("Default");
        // Архива в downloads/ нет, но source есть в config.

        var archiveSource = new PackArchiveSource
        {
            Archive = "Missing.7z",
            Sources = new Modsync.Core.Models.Manifest.Sources.ArchiveSourceRef[]
            {
                new Modsync.Core.Models.Manifest.Sources.MirrorSourceRef
                {
                    Url = "https://example.com/Missing.7z",
                    Hash = new Modsync.Core.Models.Hashing.XxHash64Value(0x5678),
                },
            },
        };

        var config = MakeConfig(archiveSources: new[] { archiveSource });
        var configPath = WriteConfigFile("with-orphan.json", config);

        var picker = new FakeFilePickerService { FileToReturn = configPath };
        var vm = Make(picker);

        await vm.LoadConfigFileCommand.ExecuteAsync(null);

        // В UI не видно — нет файла в downloads/.
        vm.UnresolvedArchives.Should().BeEmpty();

        // Но при Pack он сохраняется.
        vm.MetaName = "Test Pack";
        vm.MetaVersion = "1.0.0";
        vm.MetaAuthor = "me";
        vm.SelectedGame = "skyrimspecialedition";
        vm.GameVersion = "1.6.1170";

        PackConfigBuilderInput? captured = null;
        vm.ConfigCreated += input => captured = input;

        await vm.PackCommand.ExecuteAsync(null);

        captured.Should().NotBeNull();
        captured!.ArchiveSources.Should().HaveCount(1);
        captured.ArchiveSources[0].Archive.Should().Be("Missing.7z");
    }

    // ------------------------------------------------------------------
    //  Cancel
    // ------------------------------------------------------------------

    [Fact]
    public void Cancel_RaisesEvent()
    {
        var vm = Make();
        var raised = false;
        vm.Cancelled += () => raised = true;

        vm.CancelCommand.Execute(null);

        raised.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    //  OverwriteWarning / SavePathDisplay
    // ------------------------------------------------------------------

    [Fact]
    public async Task SavePathDisplay_WithInstance_ShowsDefaultPath()
    {
        WriteProfile("Default");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.SavePathDisplay.Should().Be(
            Path.Combine(_instancePath, "modsyncmanager-pack.json"));
    }

    [Fact]
    public async Task OverwriteWarning_ExistingFile_ShowsMessage()
    {
        WriteProfile("Default");

        // Создаём дефолтный файл.
        File.WriteAllText(
            Path.Combine(_instancePath, "modsyncmanager-pack.json"),
            "{}");

        var picker = new FakeFilePickerService { FolderToReturn = _instancePath };
        var vm = Make(picker);

        await vm.BrowseInstanceCommand.ExecuteAsync(null);

        vm.OverwriteWarning.Should().NotBeNull();
        vm.OverwriteWarning.Should().Contain("overwrite");
    }

    [Fact]
    public async Task OverwriteWarning_LoadedConfig_Suppressed()
    {
        WriteProfile("Default");
        File.WriteAllText(
            Path.Combine(_instancePath, "modsyncmanager-pack.json"),
            "{}");

        var config = MakeConfig();
        var configPath = WriteConfigFile("loaded.json", config);

        var picker = new FakeFilePickerService
        {
            FolderToReturn = _instancePath,
            FileToReturn = configPath,
        };
        var vm = Make(picker);

        await vm.LoadConfigFileCommand.ExecuteAsync(null);

        vm.OverwriteWarning.Should().BeNull();
    }

    [Fact]
    public void OverwriteWarning_NoInstance_Null()
    {
        var vm = Make();
        vm.OverwriteWarning.Should().BeNull();
    }
}
