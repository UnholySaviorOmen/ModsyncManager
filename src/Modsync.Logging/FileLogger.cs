// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.Logging;

namespace Modsync.Logging;

/// <summary>
/// ILogger, пишущий в <see cref="FileLoggerProvider"/>.
///
/// Не публичный: клиенты получают его через provider.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly FileLoggerProvider _provider;
    private readonly string _category;

    public FileLogger(FileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);

        if (exception is not null)
        {
            message += Environment.NewLine + exception;
        }

        _provider.Write(logLevel, _category, message);
    }
}
