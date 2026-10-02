// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Modsync.Gui.Shared.Services;

namespace ModsyncManager.Gui.Services;

/// <summary>
/// Реализация IProcessLauncher через Process.Start + ShellExecute.
///
/// UseShellExecute = true нужен, чтобы ОС сама разобралась, как
/// открыть файл — для .exe это запуск, для .txt — ассоциированная
/// программа. На Windows это работает из коробки.
/// </summary>
internal sealed class ShellProcessLauncher : IProcessLauncher
{
    public void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                "Path must be non-empty.", nameof(path));

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
