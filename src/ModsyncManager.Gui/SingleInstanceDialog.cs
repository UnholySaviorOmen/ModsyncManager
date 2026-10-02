// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ModsyncManager.Gui;

/// <summary>
/// Показывает нативный Windows-диалог «ModsyncManager уже запущен» и
/// завершает работу второго инстанса.
///
/// Технически — один P/Invoke на <c>MessageBoxW</c> из <c>user32.dll</c>.
/// Альтернативы (Avalonia MsBox, минимальный Application + Window)
/// неоправданно тяжёлые для одного диалога в точке входа, до
/// инициализации Avalonia.
///
/// На не-Windows платформах диалог не показывается — вместо этого
/// сообщение уходит в <c>Console.Error</c>. ModsyncManager — Windows-only
/// (см. решение 237), эта ветка нужна только чтобы код собирался
/// и не падал на Linux (dev/CI).
/// </summary>
internal static class SingleInstanceDialog
{
    private const string Title = "ModsyncManager";
    private const string Body =
        "ModsyncManager is already running. Use the existing instance.";

    // MessageBoxW flags
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_SETFOREGROUND = 0x00010000;

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int MessageBoxW(
        IntPtr hWnd,
        [param: MarshalAs(UnmanagedType.LPWStr)] string text,
        [param: MarshalAs(UnmanagedType.LPWStr)] string caption,
        uint uType);

    /// <summary>
    /// Показывает диалог (Windows) или пишет в stderr (иначе).
    /// Никогда не бросает.
    /// </summary>
    public static void Show()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                MessageBoxW(
                    IntPtr.Zero,
                    Body,
                    Title,
                    MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);
                return;
            }
            catch
            {
                // Если user32.dll вдруг не загрузится — падать не хочется.
                // Падаем в fallback ниже.
            }
        }

        try
        {
            Console.Error.WriteLine($"{Title}: {Body}");
        }
        catch
        {
            // Совсем крайний случай — молча выходим.
        }
    }
}
