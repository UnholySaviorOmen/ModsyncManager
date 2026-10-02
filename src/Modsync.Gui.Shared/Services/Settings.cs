// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Пользовательские настройки Modsync Manager.
///
/// Mutable POCO (не record) — поля меняются через SettingsVM.
/// Сохраняется в %LOCALAPPDATA%\ModsyncManager\settings.json.
///
/// Сейчас единственная настройка — DevMode. Поля логов
/// (retention, размер файла) — хардкод в FileLoggerOptions,
/// пользователь их не меняет.
/// </summary>
public sealed class Settings
{
    /// <summary>
    /// DevMode: показывает Install/Pack/Verify/Logs в сайдбаре.
    /// </summary>
    public bool DevMode { get; set; }

    /// <summary>
    /// Дефолтные настройки.
    /// </summary>
    public static Settings Default => new();
}
