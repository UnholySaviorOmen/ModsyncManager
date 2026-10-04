// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Modsync.Gui.Modules.Pack.ViewModels;
using Modsync.Gui.Modules.Pack.Views;
using Modsync.Gui.Shared.Services;
using Modsync.Pack;
using Microsoft.Extensions.Logging;

namespace ModsyncManager.Gui.Services;

internal sealed class AvaloniaPatchDialogService : IPatchDialogService
{
    private readonly PatchArchiveBuilder _builder;
    private readonly IProcessLauncher _launcher;
    private readonly ILogger<PatchArchiveDialogVM> _dialogLogger;
    private readonly ILogger<AvaloniaPatchDialogService> _logger;

    public AvaloniaPatchDialogService(
        PatchArchiveBuilder builder,
        IProcessLauncher launcher,
        ILogger<PatchArchiveDialogVM> dialogLogger,
        ILogger<AvaloniaPatchDialogService> logger)
    {
        _builder = builder;
        _launcher = launcher;
        _dialogLogger = dialogLogger;
        _logger = logger;
    }

    public async Task ShowAsync(
        string instancePath,
        int unmatchedCount,
        CancellationToken ct = default)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            _logger.LogWarning(
                "No main window — skipping patch dialog");
            return;
        }

        var vm = new PatchArchiveDialogVM(
            _builder, _launcher, _dialogLogger,
            instancePath, unmatchedCount);

        var dialog = new PatchArchiveDialog { DataContext = vm };

        vm.Closed += () => dialog.Close();

        await dialog.ShowDialog(owner);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }

        return null;
    }
}
