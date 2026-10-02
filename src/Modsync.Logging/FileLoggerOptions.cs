// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Modsync.Core;

namespace Modsync.Logging;

/// <summary>
/// Настройки файлового логгера Modsync Manager.
///
/// Намеренно простой POCO: без IOptions, без конфигурации.
/// Значения фиксированы на этапе компиляции — пользователь
/// их не меняет. RetentionDays и MaxFileSizeBytes оставлены
/// полями, чтобы тесты могли задать маленькие значения
/// (иначе тест на ротацию писал бы 10 МБ).
///
/// LogDirectory по умолчанию: %LOCALAPPDATA%\ModsyncManager\logs\.
/// Путь — из ModsyncPaths (единый источник правды).
/// </summary>
public sealed class FileLoggerOptions
{
    /// <summary>
    /// Каталог для файлов логов.
    ///
    /// По умолчанию: %LOCALAPPDATA%\ModsyncManager\logs.
    /// Создаётся при инициализации провайдера, если не существует.
    /// </summary>
    public string LogDirectory { get; set; } = ModsyncPaths.LogsDirectory;

    /// <summary>
    /// Сколько дней хранить файлы логов.
    ///
    /// Файлы старше N дней удаляются при инициализации провайдера
    /// и при каждой дневной ротации.
    ///
    /// 0 или отрицательное значение — не удалять ничего.
    ///
    /// Хардкод-значение: пользователь не меняет. Поле — для тестов.
    /// </summary>
    public int RetentionDays { get; set; } = 14;

    /// <summary>
    /// Максимальный размер файла лога в байтах.
    /// При превышении текущий файл переименовывается в .1.log,
    /// открывается новый.
    ///
    /// 0 или отрицательное значение — без ротации по размеру.
    ///
    /// Хардкод-значение: пользователь не меняет. Поле — для тестов.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Префикс имени файла. Итоговое имя:
    /// <c>{FilePrefix}-yyyy-MM-dd.log</c>.
    /// </summary>
    public string FilePrefix { get; set; } = "modsyncmanager";

    /// <summary>
    /// Минимальный уровень для записи в файл.
    /// </summary>
    public Microsoft.Extensions.Logging.LogLevel MinimumLevel { get; set; } =
        Microsoft.Extensions.Logging.LogLevel.Information;
}
