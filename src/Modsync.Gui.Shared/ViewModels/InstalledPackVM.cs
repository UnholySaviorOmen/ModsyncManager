// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Models;
using Modsync.Gui.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM одной карточки на Dashboard-дашборде.
///
/// Три команды:
///   - OpenMo2  — всегда активна. Проверяет наличие ModOrganizer.exe
///                в момент клика; если нет — выставляет WarningMessage.
///   - Install  — InstallRequested(&lt;InstancePath&gt;/modlist.json, InstancePath).
///   - Update   — диалог выбора нового modlist.json; если юзер выбрал,
///                InstallRequested(выбранный, InstancePath). Отмена — no-op.
///
/// WarningMessage — inline-предупреждение под кнопками; стирается
/// только при следующем Refresh() (карточка пересоздаётся).
/// </summary>
public sealed partial class InstalledPackVM : ViewModel
{
    private const string Mo2DirName = "MO2";
    private const string ModOrganizerExeName = "ModOrganizer.exe";
    private const string NotFoundMessage =
        "ModOrganizer.exe not found. Reinstall the pack to restore MO2.";
    private const string PickManifestTitle = "Select new modlist.json";
    private const string PickManifestFilter = ".json";

    private readonly Action<string, string> _installRequested;
    private readonly IFilePickerService _picker;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<InstalledPackVM> _logger;

    public string Name { get; }
    public string Version { get; }
    public string Game { get; }
    public string GameVersion { get; }
    public string InstancePath { get; }
    public string ManifestPath { get; }

    public string VersionText => $"v{Version}";
    public string GameText => $"{Game} · {GameVersion}";

    [ObservableProperty]
    private string? _warningMessage;

    public InstalledPackVM(
        InstalledPackInfo info,
        Action<string, string> installRequested,
        IFilePickerService picker,
        IProcessLauncher launcher,
        ILogger<InstalledPackVM> logger)
    {
        Name = info.Name;
        Version = info.Version;
        Game = info.Game;
        GameVersion = info.GameVersion;
        InstancePath = info.InstancePath;
        ManifestPath = info.ManifestPath;

        _installRequested = installRequested;
        _picker = picker;
        _launcher = launcher;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  Open MO2
    // ------------------------------------------------------------------

    [RelayCommand]
    private void OpenMo2()
    {
        WarningMessage = null;

        var mo2ExePath = Path.Combine(
            InstancePath, Mo2DirName, ModOrganizerExeName);

        if (!File.Exists(mo2ExePath))
        {
            _logger.LogWarning(
                "ModOrganizer.exe not found: {Path}", mo2ExePath);
            WarningMessage = NotFoundMessage;
            return;
        }

        try
        {
            _launcher.OpenFile(mo2ExePath);
            _logger.LogDebug(
                "Opened ModOrganizer.exe: {Path}", mo2ExePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to open ModOrganizer.exe: {Path}", mo2ExePath);
            WarningMessage = $"Failed to open ModOrganizer.exe: {ex.Message}";
        }
    }

    // ------------------------------------------------------------------
    //  Install (из манифеста в инстансе)
    // ------------------------------------------------------------------

    [RelayCommand]
    private void Install()
    {
        WarningMessage = null;
        _installRequested(ManifestPath, InstancePath);
    }

    // ------------------------------------------------------------------
    //  Update (новый манифест через диалог)
    // ------------------------------------------------------------------

    [RelayCommand]
    private async Task UpdateAsync()
    {
        WarningMessage = null;

        var picked = await _picker.PickFileAsync(
            PickManifestTitle, PickManifestFilter);

        if (string.IsNullOrWhiteSpace(picked))
        {
            _logger.LogDebug("Update cancelled by user");
            return;
        }

        _installRequested(picked, InstancePath);
    }
}
