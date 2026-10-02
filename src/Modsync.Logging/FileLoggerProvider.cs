// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Modsync.Logging;

/// <summary>
/// Провайдер логирования в файл с дневной ротацией и ротацией по размеру.
///
/// Формат имени: <c>{prefix}-yyyy-MM-dd.log</c> (по локальной дате).
/// При превышении MaxFileSizeBytes текущий файл переименовывается
/// в <c>{prefix}-yyyy-MM-dd.1.log</c> (перезапись), открывается новый.
///
/// Retention: при инициализации и при каждой дневной ротации удаляются
/// файлы старше RetentionDays. Файлы <c>.1.log</c> удаляются вместе
/// с основным файлом того же дня.
///
/// Потокобезопасность: StreamWriter закрыт lock-ом. Запись
/// синхронная (AutoFlush = true) — при крахе процесса последние
/// строки выживут.
///
/// Файл открывается с FileShare.ReadWrite — чтобы любой читатель
/// (tail, антивирус, PowerShell Get-Content, внешний log-viewer)
/// мог заглянуть в активный лог без ожидания.
///
/// Сбои I/O (нет прав, диск переполнен, файл занят) молча
/// игнорируются — логгер не должен ронять приложение.
///
/// Конструктор — с явными часами (Func&lt;DateTimeOffset&gt;).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly object _lock = new();
    private readonly Func<DateTimeOffset> _clock;

    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private bool _disposed;

    public FileLoggerProvider(
        FileLoggerOptions options,
        Func<DateTimeOffset> clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        if (string.IsNullOrWhiteSpace(_options.LogDirectory))
            throw new ArgumentException(
                "LogDirectory must be non-empty.", nameof(options));

        if (string.IsNullOrWhiteSpace(_options.FilePrefix))
            throw new ArgumentException(
                "FilePrefix must be non-empty.", nameof(options));

        EnsureDirectory();
        CleanupOldLogs();
    }

    public string LogDirectory => _options.LogDirectory;

    public string CurrentFilePath
    {
        get
        {
            var date = DateOnly.FromDateTime(_clock().LocalDateTime);
            return Path.Combine(
                _options.LogDirectory,
                $"{_options.FilePrefix}-{date:yyyy-MM-dd}.log");
        }
    }

    public ILogger CreateLogger(string categoryName)
        => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            CloseWriter();
        }
    }

    // ------------------------------------------------------------------
    //  Внутреннее API для FileLogger
    // ------------------------------------------------------------------

    internal bool IsEnabled(LogLevel level) => level >= _options.MinimumLevel;

    internal void Write(LogLevel level, string category, string message)
    {
        var now = _clock();
        var local = now.LocalDateTime;
        var today = DateOnly.FromDateTime(local);

        lock (_lock)
        {
            if (_disposed)
                return;

            try
            {
                EnsureWriter(today);

                var line = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{local:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {message}");

                RotateBySizeIfNeeded(line.Length);

                _writer!.WriteLine(line);
            }
            catch
            {
                CloseWriter();
            }
        }
    }

    // ------------------------------------------------------------------
    //  Writer management
    // ------------------------------------------------------------------

    private void EnsureWriter(DateOnly today)
    {
        if (_writer is not null && _currentDate == today)
            return;

        // Либо writer-а нет, либо сменилась дата.
        CloseWriter();

        var path = Path.Combine(
            _options.LogDirectory,
            $"{_options.FilePrefix}-{today:yyyy-MM-dd}.log");

        var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        _writer = new StreamWriter(stream, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };

        _currentDate = today;

        if (_options.RetentionDays > 0)
            CleanupOldLogs();
    }

    /// <summary>
    /// Если текущий файл + новая строка превысят MaxFileSizeBytes —
    /// закрываем writer, переименовываем в .1.log, открываем новый.
    /// </summary>
    private void RotateBySizeIfNeeded(int incomingLineLength)
    {
        if (_writer is null)
            return;

        var maxSize = _options.MaxFileSizeBytes;
        if (maxSize <= 0)
            return;

        long currentSize;
        try
        {
            currentSize = _writer.BaseStream.Length;
        }
        catch
        {
            return;
        }

        // +2 — на CRLF, который StreamWriter.WriteLine добавит.
        if (currentSize + incomingLineLength + 2 <= maxSize)
            return;

        var currentPath = Path.Combine(
            _options.LogDirectory,
            $"{_options.FilePrefix}-{_currentDate:yyyy-MM-dd}.log");

        var backupPath = Path.Combine(
            _options.LogDirectory,
            $"{_options.FilePrefix}-{_currentDate:yyyy-MM-dd}.1.log");

        CloseWriter();

        try
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            if (File.Exists(currentPath))
                File.Move(currentPath, backupPath);
        }
        catch
        {
            // Не удалось переименовать — продолжаем писать в текущий файл.
        }

        // Переоткрываем writer на тот же путь.
        try
        {
            var stream = new FileStream(
                currentPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            _writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
        }
        catch
        {
            _writer = null;
        }
    }

    private void CloseWriter()
    {
        if (_writer is null)
            return;

        try
        {
            _writer.Dispose();
        }
        catch
        {
            // Игнорируем.
        }

        _writer = null;
    }

    private void EnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(_options.LogDirectory);
        }
        catch
        {
            // Если не удалось — Write молча провалится.
        }
    }

    // ------------------------------------------------------------------
    //  Cleanup
    // ------------------------------------------------------------------

    /// <summary>
    /// Удаляет файлы вида <c>{prefix}-yyyy-MM-dd.log</c> старше
    /// RetentionDays. Также удаляет <c>.1.log</c> того же дня.
    /// Не трогает чужие файлы.
    /// </summary>
    private void CleanupOldLogs()
    {
        if (_options.RetentionDays <= 0)
            return;

        try
        {
            if (!Directory.Exists(_options.LogDirectory))
                return;

            var cutoff = DateOnly.FromDateTime(
                _clock().LocalDateTime).AddDays(-_options.RetentionDays);

            var pattern = $"{_options.FilePrefix}-*.log";

            foreach (var file in Directory.EnumerateFiles(
                _options.LogDirectory, pattern, SearchOption.TopDirectoryOnly))
            {
                if (!TryParseDateFromFileName(file, out var date))
                    continue;

                if (date >= cutoff)
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Файл занят/нет прав — пропускаем.
                }

                // Удаляем и .1.log того же дня, если есть.
                var backupPath = Path.Combine(
                    _options.LogDirectory,
                    $"{_options.FilePrefix}-{date:yyyy-MM-dd}.1.log");

                try
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                catch
                {
                    // Игнорируем.
                }
            }
        }
        catch
        {
            // Каталог недоступен — пропускаем cleanup.
        }
    }

    /// <summary>
    /// Парсит дату из имени <c>{prefix}-yyyy-MM-dd.log</c>.
    /// <c>.1.log</c> не парсится — такие файлы не удаляются отдельно.
    /// </summary>
    private bool TryParseDateFromFileName(string filePath, out DateOnly date)
    {
        date = default;

        var name = Path.GetFileNameWithoutExtension(filePath);
        var prefix = _options.FilePrefix + "-";

        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var datePart = name[prefix.Length..];

        // Отсекаем возможный суффикс ".1" — не парсим его как дату.
        var dot = datePart.IndexOf('.');
        if (dot >= 0)
            return false;

        return DateOnly.TryParseExact(
            datePart, "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
