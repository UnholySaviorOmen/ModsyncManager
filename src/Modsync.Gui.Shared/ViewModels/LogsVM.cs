// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.Input;
using Modsync.Core;
using Modsync.Gui.Shared.Services;

namespace Modsync.Gui.Shared.ViewModels;

/// <summary>
/// VM экрана Logs.
///
/// Содержит LogVM (содержимое лога) + команду «Open logs folder»,
/// перенесённую из SettingsVM.
/// </summary>
public sealed partial class LogsVM : ViewModel
{
    private readonly IProcessLauncher _launcher;

    public LogVM Log { get; }

    public LogsVM(LogVM log, IProcessLauncher launcher)
    {
        Log = log;
        _launcher = launcher;
    }

    public string LogsFolderPath => ModsyncPaths.LogsDirectory;

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            _launcher.OpenFile(LogsFolderPath);
        }
        catch
        {
            // Ошибка открытия папки не должна ронять экран.
        }
    }
}
