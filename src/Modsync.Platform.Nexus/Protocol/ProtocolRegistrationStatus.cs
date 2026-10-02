// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Статус операции ProtocolRegistrar.
/// </summary>
public enum ProtocolRegistrationStatus
{
    /// <summary>Наш handler успешно зарегистрирован.</summary>
    Registered,

    /// <summary>Наш handler уже был зарегистрирован, ничего не меняли.</summary>
    AlreadyRegistered,

    /// <summary>Восстановлен предыдущий handler (или удалена наша регистрация).</summary>
    Restored,

    /// <summary>Backup-файла не было — просто удалили нашу регистрацию.</summary>
    AlreadyRestored,

    /// <summary>Не удалось сохранить backup, регистрация отменена.</summary>
    BackupFailed,

    /// <summary>Ошибка записи в реестр.</summary>
    RegistryWriteFailed,

    /// <summary>Ошибка чтения из реестра.</summary>
    RegistryReadFailed,

    /// <summary>ModsyncManager.NxmHandler.exe не найден по ожидаемому пути.</summary>
    HandlerNotFound,
}
