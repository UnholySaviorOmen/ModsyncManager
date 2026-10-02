// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Gui.Shared.Services;

/// <summary>
/// Хранилище пользовательских настроек.
///
/// Current — всегда не null, всегда с дефолтами.
/// Load() вызывается в конструкторе реализации.
/// Save() — атомарная запись; при ошибке логирует, не бросает.
///
/// Реализация: SettingsStore.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Текущие настройки. Mutable POCO — поля меняются напрямую,
    /// после чего вызывается Save().
    /// </summary>
    Settings Current { get; }

    /// <summary>
    /// Сохранить Current в settings.json.
    /// Атомарно: temp + Move. При ошибке логирует, не бросает.
    /// </summary>
    void Save();

    /// <summary>
    /// Путь к файлу settings.json. Для диагностики и тестов.
    /// </summary>
    string FilePath { get; }
}
