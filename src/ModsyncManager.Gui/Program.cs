// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using Avalonia;
using Modsync.Gui.Shared;

namespace ModsyncManager.Gui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Single-instance: второй запуск GUI показывает диалог
        // «ModsyncManager уже запущен» и выходит. Mutex живёт всё время
        // работы первого инстанса.
        //
        // CLI (ModsyncManager.Cli.exe) этой защиты не имеет — он может
        // запускаться параллельно. Здесь mutex только для GUI.
        using var singleInstance = new SingleInstance(
            SingleInstance.DefaultMutexName);

        if (!singleInstance.IsFirstInstance)
        {
            SingleInstanceDialog.Show();
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(
                Path.GetTempPath(), "modsyncmanager-gui-crash.txt");

            try
            {
                File.WriteAllText(logPath, ex.ToString());
            }
            catch
            {
                // Если и это не получилось — печатаем в stderr.
                Console.Error.WriteLine(ex);
            }

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
