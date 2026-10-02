// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using Modsync.Core;

namespace ModsyncManager.NxmHandler;

/// <summary>
/// Реализация IHandlerLogger: append в
/// %LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log.
///
/// Особенности:
///   - Append, без ротации (кликов Mod Manager Download мало).
///   - Одна строка на событие, `[timestamp] message`, CRLF.
///   - UTF-8 без BOM.
///   - Все ошибки I/O молча игнорируются: handler не должен падать
///     из-за проблем с логом.
///   - Потокобезопасность: handler однопоточный по природе.
///   - Файл лежит рядом с GUI/CLI-логами (ModsyncPaths.LogsDirectory),
///     но пишется собственным механизмом: handler не тянет
///     Microsoft.Extensions.* (быстрый старт важнее единого формата).
///
/// Время и путь к файлу — параметры конструктора. Это позволяет
/// тестам задать контролируемые значения. Конструктор НЕ создаёт
/// директорию: это делается лениво, в Log, чтобы тесты на default
/// path не трогали реальный %LOCALAPPDATA%.
/// </summary>
public sealed class HandlerLogger : IHandlerLogger
{
    private const string DefaultFileName = "modsyncmanager-nxm-handler.log";

    private readonly string _logFilePath;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>
    /// Продакшн-конструктор: %LOCALAPPDATA%\ModsyncManager\logs\modsyncmanager-nxm-handler.log,
    /// время — DateTimeOffset.Now.
    /// </summary>
    public HandlerLogger()
        : this(DefaultPath(), () => DateTimeOffset.Now)
    {
    }

    /// <summary>
    /// Тестовый конструктор: явный путь и явные часы.
    /// </summary>
    public HandlerLogger(string logFilePath, Func<DateTimeOffset> clock)
    {
        _logFilePath = logFilePath;
        _clock = clock;
    }

    public string LogFilePath => _logFilePath;

    public void Log(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        try
        {
            // Ленивое создание директории.
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var timestamp = _clock()
                .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

            var line = $"[{timestamp}] {message}{Environment.NewLine}";

            File.AppendAllText(
                _logFilePath,
                line,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Молча игнорируем. Handler не должен падать из-за лога.
        }
    }

    private static string DefaultPath()
        => Path.Combine(ModsyncPaths.LogsDirectory, DefaultFileName);
}
