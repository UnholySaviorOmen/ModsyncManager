// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Core;

/// <summary>
/// Единый источник правды для путей Modsync Manager.
///
/// Корень: %LOCALAPPDATA%\ModsyncManager\
/// (на не-Windows — %TEMP%\ModsyncManager\ — fallback для dev/CI).
///
/// Структура:
///   %LOCALAPPDATA%\ModsyncManager\
///     settings.json
///     nexus.key
///     nxm-handler-backup.json
///     cache.db
///     logs\
///       modsyncmanager-yyyy-MM-dd.log
///       modsyncmanager-yyyy-MM-dd.1.log
///       modsyncmanager-nxm-handler.log
///
/// Все пути вычисляются один раз при старте процесса (статический
/// конструктор). Root иммутабелен — в течение жизни процесса
/// не меняется.
///
/// Не создаёт папки сам: создание — ответственность того, кто
/// пишет файл (SettingsStore, NexusApiKeyProvider, FileLoggerProvider,
/// SqliteHashCache). Это позволяет тестам подменять путь через
/// конструктор и не трогать реальный %LOCALAPPDATA%.
/// </summary>
public static class ModsyncPaths
{
    /// <summary>
    /// Корень Modsync Manager: %LOCALAPPDATA%\ModsyncManager\.
    /// </summary>
    public static string Root { get; } = ComputeRoot();

    /// <summary>
    /// %LOCALAPPDATA%\ModsyncManager\logs\.
    /// </summary>
    public static string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>
    /// %LOCALAPPDATA%\ModsyncManager\settings.json.
    /// </summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>
    /// %LOCALAPPDATA%\ModsyncManager\nexus.key.
    /// </summary>
    public static string NexusKeyFile => Path.Combine(Root, "nexus.key");

    /// <summary>
    /// %LOCALAPPDATA%\ModsyncManager\nxm-handler-backup.json.
    /// </summary>
    public static string NxmHandlerBackupFile =>
        Path.Combine(Root, "nxm-handler-backup.json");

    /// <summary>
    /// %LOCALAPPDATA%\ModsyncManager\cache.db.
    /// SQLite-база кеша хешей файлов.
    /// </summary>
    public static string CacheDbFile => Path.Combine(Root, "cache.db");

    private static string ComputeRoot()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(localAppData))
        {
            // Не-Windows: LocalApplicationData может быть пустым.
            // Modsync Manager — Windows-only (решение 237), ветка для dev/CI.
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "ModsyncManager");
    }
}
