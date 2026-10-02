// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;
using Modsync.Gui.Shared.ViewModels;

namespace ModsyncManager.Gui.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsVM vm)
        {
            // Fire-and-forget: результат придёт через обновление свойств VM.
            // Валидируем при каждом заходе — статус мог поменяться из CLI
            // или из другой вкладки.
            _ = vm.Nexus.RefreshAsync();
            _ = vm.NexusFreeDownload.RefreshAsync();
        }
    }
}
