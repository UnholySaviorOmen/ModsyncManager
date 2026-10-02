// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Результат операции ProtocolRegistrar.
///
/// Никогда не null. Все методы Registrar возвращают результат,
/// даже при ошибке. Исключения не бросаются наружу.
/// </summary>
public sealed record ProtocolRegistrationResult
{
    /// <summary>Статус операции.</summary>
    public required ProtocolRegistrationStatus Status { get; init; }

    /// <summary>
    /// Дополнительное сообщение (для логов, UI). Может быть null.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Путь к предыдущему handler-у (если был сохранён или восстановлен).
    /// Для диагностики и UI. Может быть null.
    /// </summary>
    public string? PreviousHandlerPath { get; init; }

    /// <summary>
    /// true, если операция завершилась успешно (Registered/AlreadyRegistered/Restored/AlreadyRestored).
    /// </summary>
    public bool IsSuccess => Status is
        ProtocolRegistrationStatus.Registered or
        ProtocolRegistrationStatus.AlreadyRegistered or
        ProtocolRegistrationStatus.Restored or
        ProtocolRegistrationStatus.AlreadyRestored;

    // ------------------------------------------------------------------
    //  Фабрики
    // ------------------------------------------------------------------

    public static ProtocolRegistrationResult Registered(string? previous = null)
        => new()
        {
            Status = ProtocolRegistrationStatus.Registered,
            PreviousHandlerPath = previous,
        };

    public static ProtocolRegistrationResult AlreadyRegistered()
        => new() { Status = ProtocolRegistrationStatus.AlreadyRegistered };

    public static ProtocolRegistrationResult Restored(string? previous = null)
        => new()
        {
            Status = ProtocolRegistrationStatus.Restored,
            PreviousHandlerPath = previous,
        };

    public static ProtocolRegistrationResult AlreadyRestored()
        => new() { Status = ProtocolRegistrationStatus.AlreadyRestored };

    public static ProtocolRegistrationResult BackupFailed(string message)
        => new()
        {
            Status = ProtocolRegistrationStatus.BackupFailed,
            Message = message,
        };

    public static ProtocolRegistrationResult RegistryWriteFailed(string message)
        => new()
        {
            Status = ProtocolRegistrationStatus.RegistryWriteFailed,
            Message = message,
        };

    public static ProtocolRegistrationResult RegistryReadFailed(string message)
        => new()
        {
            Status = ProtocolRegistrationStatus.RegistryReadFailed,
            Message = message,
        };

    public static ProtocolRegistrationResult HandlerNotFound(string path)
        => new()
        {
            Status = ProtocolRegistrationStatus.HandlerNotFound,
            Message = $"ModsyncManager.NxmHandler.exe not found: {path}",
        };
}
