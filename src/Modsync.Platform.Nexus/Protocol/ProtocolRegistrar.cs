// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Реализация IProtocolRegistrar поверх IRegistryAccessor.
///
/// Пути в реестре:
///     HKCU\Software\Classes\nxm
///         (Default) = "URL:NXM Protocol"
///         "URL Protocol" = ""
///
///     HKCU\Software\Classes\nxm\shell\open\command
///         (Default) = "\"<handler-path>\" \"%1\""
///
/// Backup-файл: %LOCALAPPDATA%\ModsyncManager\nxm-handler-backup.json
/// (путь — из ModsyncPaths, единый корень).
///
/// Handler-путь: <AppContext.BaseDirectory>/ModsyncManager.NxmHandler.exe
///
/// Все параметры (registryAccessor, backupFilePath, handlerPath)
/// параметризованы для тестов.
/// </summary>
public sealed class ProtocolRegistrar : IProtocolRegistrar
{
    // ------------------------------------------------------------------
    //  Registry paths
    // ------------------------------------------------------------------

    internal const string NxmKeyPath = @"Software\Classes\nxm";
    internal const string CommandKeyPath = @"Software\Classes\nxm\shell\open\command";
    internal const string UrlProtocolValueName = "URL Protocol";
    internal const string DefaultValueName = null!; // (Default)

    private const string DefaultValueData = "URL:NXM Protocol";
    private const string UrlProtocolValueData = "";

    // ------------------------------------------------------------------
    //  Defaults
    // ------------------------------------------------------------------

    private static readonly string DefaultBackupPath =
        ModsyncPaths.NxmHandlerBackupFile;

    private const string HandlerExeName = "ModsyncManager.NxmHandler.exe";

    // ------------------------------------------------------------------
    //  Fields
    // ------------------------------------------------------------------

    private readonly IRegistryAccessor _registry;
    private readonly string _backupFilePath;
    private readonly string _handlerPath;
    private readonly ILogger<ProtocolRegistrar> _logger;

    // ------------------------------------------------------------------
    //  Constructors
    // ------------------------------------------------------------------

    public ProtocolRegistrar(
        IRegistryAccessor registry,
        ILogger<ProtocolRegistrar> logger)
        : this(
            registry,
            DefaultBackupPath,
            Path.Combine(AppContext.BaseDirectory, HandlerExeName),
            logger)
    {
    }

    public ProtocolRegistrar(
        IRegistryAccessor registry,
        string backupFilePath,
        string handlerPath,
        ILogger<ProtocolRegistrar> logger)
    {
        _registry = registry;
        _backupFilePath = backupFilePath;
        _handlerPath = handlerPath;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    //  Public API
    // ------------------------------------------------------------------

    public ProtocolRegistrationResult Register()
    {
        // 1. Проверяем, что handler существует.
        if (!File.Exists(_handlerPath))
        {
            _logger.LogWarning(
                "ModsyncManager.NxmHandler.exe not found at {Path}",
                _handlerPath);
            return ProtocolRegistrationResult.HandlerNotFound(_handlerPath);
        }

        // 2. Читаем текущее состояние.
        var current = ReadCurrentHandler();

        // 3. Если уже наш handler — ничего не делаем.
        if (IsOurHandler(current))
        {
            _logger.LogDebug(
                "nxm:// is already registered to our handler.");
            return ProtocolRegistrationResult.AlreadyRegistered();
        }

        // 4. Если чужой handler — сохраняем backup.
        string? previousPath = null;

        if (current is not null)
        {
            previousPath = NxmHandlerBackup.ExtractExePathFromCommand(
                current.Command);

            var backup = new NxmHandlerBackup
            {
                PreviousCommand = current.Command,
                PreviousDefaultValue = current.DefaultValue,
                PreviousUrlProtocolValue = current.UrlProtocolValue,
                BackupTimeUtc = DateTimeOffset.UtcNow,
                PreviousHandlerPath = previousPath,
            };

            try
            {
                backup.SaveToFile(_backupFilePath);
                _logger.LogInformation(
                    "Saved nxm:// handler backup to {Path} " +
                    "(previous handler: {Previous})",
                    _backupFilePath, previousPath ?? "<unknown>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to save nxm:// handler backup to {Path}",
                    _backupFilePath);
                return ProtocolRegistrationResult.BackupFailed(
                    $"Failed to save backup to '{_backupFilePath}': " +
                    $"{ex.Message}");
            }
        }

        // 5. Пишем нашу регистрацию.
        try
        {
            WriteOurRegistration();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write nxm:// registration to HKCU.");
            return ProtocolRegistrationResult.RegistryWriteFailed(
                $"Failed to write registry: {ex.Message}");
        }

        _logger.LogInformation(
            "nxm:// registered to {Handler}", _handlerPath);

        return ProtocolRegistrationResult.Registered(previousPath);
    }

    public ProtocolRegistrationResult Restore()
    {
        // 1. Пытаемся загрузить backup.
        NxmHandlerBackup? backup = null;

        try
        {
            backup = NxmHandlerBackup.LoadFromFile(_backupFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read backup file {Path}, " +
                "will fall back to simple removal.",
                _backupFilePath);
        }

        try
        {
            if (backup is null)
            {
                // 2a. Backup-файла нет — просто удаляем нашу регистрацию.
                RemoveOurRegistration();

                _logger.LogInformation(
                    "nxm:// handler removed (no backup available).");
                return ProtocolRegistrationResult.AlreadyRestored();
            }

            // 2b. Backup есть — восстанавливаем.
            RestoreFromBackup(backup);

            // 3. Удаляем backup-файл (он больше не нужен).
            TryDeleteBackupFile();

            _logger.LogInformation(
                "nxm:// handler restored to {Previous}",
                backup.PreviousHandlerPath ?? "<unknown>");

            return ProtocolRegistrationResult.Restored(
                backup.PreviousHandlerPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to restore nxm:// handler.");
            return ProtocolRegistrationResult.RegistryWriteFailed(
                $"Failed to restore: {ex.Message}");
        }
    }

    public ProtocolRegistrationState GetState()
    {
        try
        {
            var current = ReadCurrentHandler();

            if (current is null)
                return ProtocolRegistrationState.NotRegistered;

            return IsOurHandler(current)
                ? ProtocolRegistrationState.RegisteredToUs
                : ProtocolRegistrationState.RegisteredToOther;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read nxm:// registration state.");
            return ProtocolRegistrationState.Unknown;
        }
    }

    // ------------------------------------------------------------------
    //  Internals: registry
    // ------------------------------------------------------------------

    private sealed record CurrentHandler(
        string? Command,
        string? DefaultValue,
        string? UrlProtocolValue);

    private CurrentHandler? ReadCurrentHandler()
    {
        if (!_registry.KeyExists(NxmKeyPath))
            return null;

        return new CurrentHandler(
            Command: _registry.GetValue(CommandKeyPath, DefaultValueName),
            DefaultValue: _registry.GetValue(NxmKeyPath, DefaultValueName),
            UrlProtocolValue: _registry.GetValue(
                NxmKeyPath, UrlProtocolValueName));
    }

    private bool IsOurHandler(CurrentHandler? current)
    {
        if (current is null)
            return false;

        if (string.IsNullOrWhiteSpace(current.Command))
            return false;

        var commandPath = NxmHandlerBackup.ExtractExePathFromCommand(
            current.Command);

        if (commandPath is null)
            return false;

        return string.Equals(
            Path.GetFullPath(commandPath),
            Path.GetFullPath(_handlerPath),
            StringComparison.OrdinalIgnoreCase);
    }

    private void WriteOurRegistration()
    {
        _registry.SetValue(NxmKeyPath, DefaultValueName, DefaultValueData);
        _registry.SetValue(
            NxmKeyPath, UrlProtocolValueName, UrlProtocolValueData);

        var command = $"\"{_handlerPath}\" \"%1\"";
        _registry.SetValue(CommandKeyPath, DefaultValueName, command);
    }

    private void RemoveOurRegistration()
    {
        _registry.DeleteKey(NxmKeyPath, recursive: true);
    }

    private void RestoreFromBackup(NxmHandlerBackup backup)
    {
        if (_registry.KeyExists(NxmKeyPath))
            _registry.DeleteKey(NxmKeyPath, recursive: true);

        if (backup.PreviousDefaultValue is not null)
        {
            _registry.SetValue(
                NxmKeyPath, DefaultValueName, backup.PreviousDefaultValue);
        }

        if (backup.PreviousUrlProtocolValue is not null)
        {
            _registry.SetValue(
                NxmKeyPath, UrlProtocolValueName,
                backup.PreviousUrlProtocolValue);
        }

        if (backup.PreviousCommand is not null)
        {
            _registry.SetValue(
                CommandKeyPath, DefaultValueName, backup.PreviousCommand);
        }
    }

    private void TryDeleteBackupFile()
    {
        try
        {
            if (File.Exists(_backupFilePath))
                File.Delete(_backupFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete backup file {Path}",
                _backupFilePath);
        }
    }
}
