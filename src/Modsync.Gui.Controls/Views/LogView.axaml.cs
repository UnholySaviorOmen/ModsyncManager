// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Modsync.Gui.Shared.ViewModels;

namespace Modsync.Gui.Controls.Views;

public partial class LogView : UserControl
{
    private ScrollViewer? _scroll;

    public LogView()
    {
        InitializeComponent();

        _scroll = this.FindControl<ScrollViewer>("Scroll");

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is LogVM vm)
        {
            vm.Entries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        if (_scroll is null) return;

        Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd());
    }
}
