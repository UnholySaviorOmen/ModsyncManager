using FluentAssertions;
using Modsync.Gui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Gui.Shared.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public SettingsStoreTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(), "modsyncmanager-settings-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private SettingsStore MakeStore()
        => new(_settingsPath, NullLogger<SettingsStore>.Instance);

    // ------------------------------------------------------------------
    //  Load
    // ------------------------------------------------------------------

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        var store = MakeStore();

        store.Current.DevMode.Should().BeFalse();
    }

    [Fact]
    public void Load_ValidFile_ReadsValues()
    {
        File.WriteAllText(_settingsPath,
            """
            {
              "devMode": true
            }
            """);

        var store = MakeStore();

        store.Current.DevMode.Should().BeTrue();
    }

    [Fact]
    public void Load_MalformedJson_UsesDefaults()
    {
        File.WriteAllText(_settingsPath, "{ not valid json }");

        var store = MakeStore();

        store.Current.DevMode.Should().BeFalse();
    }

    [Fact]
    public void Load_EmptyFile_UsesDefaults()
    {
        File.WriteAllText(_settingsPath, "");

        var store = MakeStore();

        store.Current.DevMode.Should().BeFalse();
    }

    [Fact]
    public void Load_PartialJson_UsesDefaultsForMissing()
    {
        File.WriteAllText(_settingsPath, "{}");

        var store = MakeStore();

        store.Current.DevMode.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  Save
    // ------------------------------------------------------------------

    [Fact]
    public void Save_WritesFile()
    {
        var store = MakeStore();
        store.Current.DevMode = true;
        store.Save();

        File.Exists(_settingsPath).Should().BeTrue();

        var json = File.ReadAllText(_settingsPath);
        json.Should().Contain("\"devMode\": true");
    }

    [Fact]
    public void Save_Roundtrip_PreservesValues()
    {
        var store1 = MakeStore();
        store1.Current.DevMode = true;
        store1.Save();

        var store2 = MakeStore();

        store2.Current.DevMode.Should().BeTrue();
    }

    [Fact]
    public void Save_CreatesDirectory()
    {
        var nested = Path.Combine(_tempDir, "a", "b", "settings.json");
        var store = new SettingsStore(nested, NullLogger<SettingsStore>.Instance);

        store.Save();

        File.Exists(nested).Should().BeTrue();
    }

    [Fact]
    public void Save_NoTempFileLeftBehind()
    {
        var store = MakeStore();
        store.Save();

        File.Exists(_settingsPath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        var store = MakeStore();
        store.Current.DevMode = true;
        store.Save();

        store.Current.DevMode = false;
        store.Save();

        var store2 = MakeStore();
        store2.Current.DevMode.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    //  FilePath
    // ------------------------------------------------------------------

    [Fact]
    public void FilePath_ExposesPath()
    {
        var store = MakeStore();
        store.FilePath.Should().Be(_settingsPath);
    }

    // ------------------------------------------------------------------
    //  Default values
    // ------------------------------------------------------------------

    [Fact]
    public void Default_HasExpectedValues()
    {
        var s = Settings.Default;

        s.DevMode.Should().BeFalse();
    }
}
