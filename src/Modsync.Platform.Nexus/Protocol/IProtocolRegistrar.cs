// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Управление регистрацией nxm:// в HKCU.
///
/// Все методы:
///   - не бросают исключений;
///   - возвращают ProtocolRegistrationResult;
///   - идемпотентны (повторный вызов не ломает состояние).
/// </summary>
public interface IProtocolRegistrar
{
    /// <summary>
    /// Регистрирует наш handler как обработчик nxm://.
    ///
    /// Если уже зарегистрирован наш handler — возвращает
    /// AlreadyRegistered, ничего не меняет.
    ///
    /// Если зарегистрирован чужой handler — сохраняет backup
    /// и перезаписывает.
    /// </summary>
    ProtocolRegistrationResult Register();

    /// <summary>
    /// Восстанавливает предыдущий handler из backup-файла.
    ///
    /// Если backup-файла нет — просто удаляет нашу регистрацию
    /// (возвращает AlreadyRestored).
    /// </summary>
    ProtocolRegistrationResult Restore();

    /// <summary>
    /// Текущее состояние регистрации.
    /// </summary>
    ProtocolRegistrationState GetState();
}

/// <summary>
/// Состояние регистрации nxm://.
/// </summary>
public enum ProtocolRegistrationState
{
    /// <summary>nxm:// не зарегистрирован ни за кем.</summary>
    NotRegistered,

    /// <summary>Зарегистрирован наш handler.</summary>
    RegisteredToUs,

    /// <summary>Зарегистрирован чужой handler (Vortex, MO2, etc.).</summary>
    RegisteredToOther,

    /// <summary>Не удалось прочитать состояние реестра.</summary>
    Unknown,
}
