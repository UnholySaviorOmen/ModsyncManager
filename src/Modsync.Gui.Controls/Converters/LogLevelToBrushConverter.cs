// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Microsoft.Extensions.Logging;

using AvaloniaColor = Avalonia.Media.Color;

namespace Modsync.Gui.Controls.Converters;

/// <summary>
/// Цвета уровней логов под палитру Modsync Manager.
/// Обновлено в 3.9.4 — приглушённые нейтральные + семантические.
/// </summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public static readonly LogLevelToBrushConverter Instance = new();

    // Trace/Debug — приглушённые нейтральные (не отвлекают)
    private static readonly IBrush TraceBrush = new SolidColorBrush(AvaloniaColor.Parse("#5a5a5a"));
    private static readonly IBrush DebugBrush = new SolidColorBrush(AvaloniaColor.Parse("#7a7a7a"));

    // Information — основной текст
    private static readonly IBrush InfoBrush = new SolidColorBrush(AvaloniaColor.Parse("#e8e8e8"));

    // Warning — акцент (наш тёплый песочный)
    private static readonly IBrush WarningBrush = new SolidColorBrush(AvaloniaColor.Parse("#d3b181"));

    // Error — приглушённый красный
    private static readonly IBrush ErrorBrush = new SolidColorBrush(AvaloniaColor.Parse("#d97777"));

    // Critical — насыщенный, но не кричащий
    private static readonly IBrush CriticalBrush = new SolidColorBrush(AvaloniaColor.Parse("#c85a5a"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LogLevel level) return InfoBrush;

        return level switch
        {
            LogLevel.Trace => TraceBrush,
            LogLevel.Debug => DebugBrush,
            LogLevel.Information => InfoBrush,
            LogLevel.Warning => WarningBrush,
            LogLevel.Error => ErrorBrush,
            LogLevel.Critical => CriticalBrush,
            _ => InfoBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
