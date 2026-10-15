// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Пользовательские настройки ModsyncManager.
///
/// Mutable POCO (не record) — поля меняются через SettingsVM.
/// Сохраняется в %LOCALAPPDATA%\ModsyncManager\settings.json.
///
/// Culture — код языка интерфейса ("en" или "ru"). Default — "ru".
/// Применяется при старте приложения в App.InitializeLocalization.
/// Смена языка в UI требует перезапуска.
/// </summary>
public sealed class Settings
{
    /// <summary>
    /// DevMode: показывает Install/Pack/Verify/Logs/Cache в сайдбаре.
    /// </summary>
    public bool DevMode { get; set; }

    /// <summary>
    /// Код языка интерфейса. "ru" (default) или "en".
    /// Читается App.InitializeLocalization при старте.
    /// </summary>
    public string Culture { get; set; } = "ru";

    public static Settings Default => new();
}
