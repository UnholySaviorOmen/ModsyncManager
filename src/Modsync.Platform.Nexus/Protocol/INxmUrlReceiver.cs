// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Приёмник nxm:// URL от ModsyncManager.NxmHandler.exe.
///
/// Серверная сторона named pipe \\.\pipe\modsyncmanager-nxm.
/// Живёт всё время работы ModsyncManager. Запускается вручную
/// (App.axaml.cs GUI) через StartAsync, останавливается
/// при выходе через StopAsync / DisposeAsync.
///
/// URL-ы складываются в очередь (Channel). WaitForUrlAsync
/// забирает первый доступный URL из очереди.
///
/// FIFO: URLs обрабатываются в порядке поступления.
/// Очередь неограниченная: если пользователь накликает 100 URL
/// подряд — все попадут в память. Ограничение — v0.3.0.
/// </summary>
public interface INxmUrlReceiver
{
    /// <summary>
    /// Запускает фоновый цикл приёма URLs.
    ///
    /// Повторный вызов до StopAsync — InvalidOperationException.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Останавливает цикл приёма. Идемпотентно.
    /// Закрывает Channel — все ожидающие WaitForUrlAsync
    /// получат null.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Ждёт первый доступный URL из очереди.
    ///
    /// Возвращает:
    ///   - URL, если он есть (или появится);
    ///   - null, если receiver остановлен (Channel закрыт);
    ///   - OperationCanceledException, если ct отменён.
    /// </summary>
    Task<string?> WaitForUrlAsync(CancellationToken ct);

    /// <summary>
    /// Не блокируя, выбрасывает все URL, накопившиеся в очереди.
    /// Возвращает количество выброшенных URL.
    ///
    /// Используется NexusFreeNxmProvider в начале каждого
    /// RequestNxmUrlAsync: URL-ы от предыдущих сессий install
    /// (пользователь отменил, URL уже пришёл) не должны попасть
    /// в текущий install. Иначе NexusDownloader сверит
    /// game/modId/fileId и упадёт с InvalidOperationException.
    ///
    /// Если очередь пуста — возвращает 0, ничего не делает.
    /// Если receiver не запущен — тоже возвращает 0
    /// (нечего выбрасывать).
    ///
    /// Не бросает. Не блокирует.
    /// </summary>
    int TryDrainPendingUrls();

    /// <summary>true, если StartAsync был вызван и StopAsync не был.</summary>
    bool IsRunning { get; }
}
