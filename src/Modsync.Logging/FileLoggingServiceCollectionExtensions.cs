// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Modsync.Logging;

/// <summary>
/// DI-extension для файлового логгера.
///
/// Использование:
/// <code>
/// services.AddLogging(builder =>
/// {
///     builder.AddSimpleConsole(...);
///     builder.AddFileLogger();
/// });
/// </code>
///
/// Регистрируется через TryAddEnumerable с явным
/// ImplementationType (FileLoggerProvider). Без этого
/// TryAddEnumerable бросает ArgumentException: он не умеет
/// различать регистрации ILoggerProvider, когда
/// ImplementationType == null (фабрика без указания типа).
/// </summary>
public static class FileLoggingServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует <see cref="FileLoggerProvider"/> как ILoggerProvider.
    ///
    /// Если <paramref name="configure"/> == null — используются
    /// дефолтные опции (RetentionDays = 14, MaxFileSizeBytes = 10 МБ,
    /// LogDirectory = %LOCALAPPDATA%\ModsyncManager\logs\).
    /// </summary>
    public static ILoggingBuilder AddFileLogger(
        this ILoggingBuilder builder,
        Action<FileLoggerOptions>? configure = null)
    {
        var options = new FileLoggerOptions();
        configure?.Invoke(options);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>(
                _ => new FileLoggerProvider(
                    options,
                    () => DateTimeOffset.Now)));

        return builder;
    }
}
