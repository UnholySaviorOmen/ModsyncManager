// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Абстракция над реестром Windows.
///
/// Все пути — ОТНОСИТЕЛЬНЫЕ от HKEY_CURRENT_USER. Например:
///     "Software\\Classes\\nxm"
///     "Software\\ModsyncManager\\Tests\\a1b2c3d4"
///
/// HKCU выбран сознательно: регистрация nxm:// не требует
/// админских прав и не влияет на других пользователей машины.
/// HKLM в интерфейсе отсутствует — не даём возможности случайно
/// туда залезть.
///
/// Абстракция нужна для тестов: fake-аксессор проверяет логику
/// без реального реестра (не портит рабочую среду разработчика).
/// Один smoke-тест использует реальный WindowsRegistryAccessor
/// с временной веткой "Software\\ModsyncManager\\Tests\\{guid}".
/// </summary>
public interface IRegistryAccessor
{
    /// <summary>
    /// Читает значение. Возвращает null, если ключа или значения нет.
    /// Не бросает.
    /// </summary>
    string? GetValue(string keyPath, string? valueName);

    /// <summary>
    /// Пишет значение (создаёт ключ, если нужно).
    /// Бросает IOException / UnauthorizedAccessException при ошибке.
    /// </summary>
    void SetValue(string keyPath, string? valueName, string value);

    /// <summary>
    /// Удаляет ключ. recursive=true — со всеми подключами.
    /// Не бросает, если ключа нет.
    /// Бросает IOException при ошибке удаления.
    /// </summary>
    void DeleteKey(string keyPath, bool recursive);

    /// <summary>
    /// Удаляет значение. Не бросает, если значения нет.
    /// </summary>
    void DeleteValue(string keyPath, string? valueName);

    /// <summary>
    /// true, если ключ существует.
    /// Не бросает.
    /// </summary>
    bool KeyExists(string keyPath);
}
