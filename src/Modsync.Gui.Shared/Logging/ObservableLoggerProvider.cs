// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Logging;

public sealed class ObservableLoggerProvider : ILoggerProvider
{
    private readonly ObservableLogSink _sink;
    private readonly LogLevel _minLevel;

    public ObservableLoggerProvider(ObservableLogSink sink, LogLevel minLevel = LogLevel.Information)
    {
        _sink = sink;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
        => new ObservableLogger(_sink, categoryName, _minLevel);

    public void Dispose() { }

    private sealed class ObservableLogger : ILogger
    {
        private readonly ObservableLogSink _sink;
        private readonly string _category;
        private readonly LogLevel _minLevel;

        public ObservableLogger(ObservableLogSink sink, string category, LogLevel minLevel)
        {
            _sink = sink;
            _category = category;
            _minLevel = minLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (exception is not null)
                message += $" | {exception.GetType().Name}: {exception.Message}";

            _sink.Add(new LogEntry(DateTimeOffset.Now, logLevel, message));
        }
    }
}
