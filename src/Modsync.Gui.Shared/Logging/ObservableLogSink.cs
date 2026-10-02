// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Modsync.Gui.Shared.Logging;

/// <summary>
/// Потокобезопасный приёмник логов.
///
/// Все мутации Entries идут через IUiDispatcher (если он задан),
/// чтобы ObservableCollection мутировался строго на UI-потоке.
/// Если диспетчер не задан (тесты, не-GUI сценарии) — мутации
/// синхронные, как раньше.
///
/// Порядок логов сохраняется: IUiDispatcher.Post в Avalonia
/// гарантирует FIFO-порядок обработки в UI-очереди.
///
/// Хранится последние MaxEntries записей; старые вытесняются.
/// </summary>
public sealed class ObservableLogSink
{
    private const int MaxEntries = 200;
    private readonly object _lock = new();
    private readonly IUiDispatcher? _dispatcher;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ObservableLogSink(IUiDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
    }

    public void Add(LogEntry entry)
    {
        if (_dispatcher is null)
        {
            AppendCore(entry);
            return;
        }

        _dispatcher.Post(() => AppendCore(entry));
    }

    public void Clear()
    {
        if (_dispatcher is null)
        {
            ClearCore();
            return;
        }

        _dispatcher.Post(ClearCore);
    }

    private void AppendCore(LogEntry entry)
    {
        lock (_lock)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }
    }

    private void ClearCore()
    {
        lock (_lock)
        {
            Entries.Clear();
        }
    }
}
