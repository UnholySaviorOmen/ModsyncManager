// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Reflection;
using CodingSeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM экрана Settings. Секции:
///   - About (ProductName, Version, Copyright, License).
///   - Language (en / ru).
///   - Nexus Mods (API key).
///   - Nexus Free Download (nxm:// handler).
///   - Application settings (DevMode).
///
/// Смена языка: SelectedCulture → SettingsStore.Save + подсказка
/// «Restart ModsyncManager». Loc.Instance.CurrentLanguage не
/// меняем на лету, чтобы не пересобирать все UI-биндинги — читаем
/// при следующем старте в App.InitializeLocalization.
/// </summary>
public sealed partial class SettingsVM : ViewModel
{
    private readonly ISettingsStore _settings;

    public NexusSettingsVM Nexus { get; }
    public NexusFreeDownloadSettingsVM NexusFreeDownload { get; }

    public ObservableCollection<LanguageOption> AvailableCultures { get; } = new()
    {
        new LanguageOption("en", "Settings.Language_English"),
        new LanguageOption("ru", "Settings.Language_Russian"),
    };

    private LanguageOption? _selectedCulture;

    public LanguageOption? SelectedCulture
    {
        get => _selectedCulture;
        set
        {
            if (value is null) return;
            if (_selectedCulture?.Code == value.Code) return;

            _selectedCulture = value;
            _settings.Current.Culture = value.Code;
            _settings.Save();

            OnPropertyChanged();
            OnPropertyChanged(nameof(LanguageChangedHint));
        }
    }

    public bool HasLanguageChanged =>
        !string.Equals(
            _settings.Current.Culture,
            _selectedCulture?.Code ?? "en",
            StringComparison.Ordinal);

    public string LanguageChangedHint => Loc.Tr("Settings.Language_RestartHint");

    public SettingsVM(
        NexusSettingsVM nexus,
        NexusFreeDownloadSettingsVM nexusFreeDownload,
        ISettingsStore settings)
    {
        Nexus = nexus;
        NexusFreeDownload = nexusFreeDownload;
        _settings = settings;

        _selectedCulture = AvailableCultures
            .FirstOrDefault(c => c.Code == _settings.Current.Culture)
            ?? AvailableCultures[0];
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

/// <summary>
/// Опция языка для ComboBox. Key — ключ локализации названия
/// (например, "Settings.Language_English").
/// </summary>
public sealed record LanguageOption(string Code, string DisplayNameKey)
{
    public string DisplayName => Loc.Tr(DisplayNameKey);
    public override string ToString() => DisplayName;
}
