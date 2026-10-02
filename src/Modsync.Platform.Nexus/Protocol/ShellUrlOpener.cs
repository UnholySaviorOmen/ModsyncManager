// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Реализация IUrlOpener через Process.Start с UseShellExecute.
///
/// UseShellExecute = true нужен, чтобы Windows сама разобралась,
/// как открыть URL. Для https:// это откроет браузер пользователя.
///
/// Если запуск невозможен — бросает InvalidOperationException
/// с понятным сообщением. Вызывающая сторона (NexusFreeNxmProvider)
/// логирует и продолжает (или возвращает null).
/// </summary>
public sealed class ShellUrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException(
                "URL must be non-empty.", nameof(url));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to open URL in the default browser: {ex.Message}",
                ex);
        }
    }
}
