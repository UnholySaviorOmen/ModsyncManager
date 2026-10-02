// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM экрана Settings. Секции:
///   - About (ProductName, Version, Copyright, License).
///   - Nexus Mods (API key).
///   - Nexus Free Download (nxm:// handler).
///   - Application settings (DevMode).
///
/// Cache и Logs переехали на отдельные DevMode-экраны (CacheVM, LogsVM).
/// DevMode — прокси на ISettingsStore.Current.DevMode.
/// </summary>
public sealed partial class SettingsVM : ViewModel
{
    private readonly ISettingsStore _settings;

    public NexusSettingsVM Nexus { get; }
    public NexusFreeDownloadSettingsVM NexusFreeDownload { get; }

    public SettingsVM(
        NexusSettingsVM nexus,
        NexusFreeDownloadSettingsVM nexusFreeDownload,
        ISettingsStore settings)
    {
        Nexus = nexus;
        NexusFreeDownload = nexusFreeDownload;
        _settings = settings;
    }

    // ------------------------------------------------------------------
    //  About
    // ------------------------------------------------------------------

    public string ProductName => "ModsyncManager";
    public string Version { get; } = GetVersion();
    public string Copyright => "Copyright (C) 2026 UnholySaviorOmen";
    public string License => "GPL-3.0-only";
    public string Footer =>
        $"{ProductName} v{Version} · {License} · {Copyright}";

    private static string GetVersion()
    {
        var informational = typeof(SettingsVM).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return "0.0.0";
    }

    // ------------------------------------------------------------------
    //  DevMode
    // ------------------------------------------------------------------

    public bool IsDevMode
    {
        get => _settings.Current.DevMode;
        set
        {
            if (_settings.Current.DevMode == value) return;
            _settings.Current.DevMode = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }
}
