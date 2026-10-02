// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Modsync.Gui.Shared.Logging;

namespace Modsync.Gui.Shared.ViewModels;

public sealed partial class LogVM : ViewModel
{
    private readonly ObservableLogSink _sink;

    public LogVM(ObservableLogSink sink)
    {
        _sink = sink;
    }

    public ObservableCollection<LogEntry> Entries => _sink.Entries;

    [RelayCommand]
    public void Clear() => _sink.Clear();
}
