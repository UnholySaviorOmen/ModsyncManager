// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Pipes;
using Modsync.Core.Nxm;

namespace ModsyncManager.NxmHandler;

/// <summary>
/// Клиент named pipe к работающему ModsyncManager.
///
/// ModsyncManager слушает на \\.\pipe\modsyncmanager-nxm (сервер — NxmUrlReceiver
/// в Modsync.Platform.Nexus). Handler подключается, пишет URL
/// одной строкой, закрывает соединение.
///
/// Контракт:
///   - TrySendAsync НИКОГДА не бросает.
///   - Возвращает true, если URL успешно отправлен.
///   - Возвращает false, если pipe недоступен, timeout истёк,
///     или произошла любая ошибка I/O.
///
/// Почему не бросаем: handler — системный callback. Если что-то
/// пошло не так — handler должен тихо завершиться (Windows покажет
/// пользователю «не удалось открыть»). Никаких MessageBox-ов,
/// никаких стектрейсов.
///
/// Timeout: 2 секунды. Если ModsyncManager висит (например, на UI-потоке) —
/// handler не должен блокировать пользователя.
/// </summary>
public static class PipeClient
{
    /// <summary>
    /// Имя pipe. Единый источник правды — Modsync.Core.Nxm.NxmPipeName.
    /// Значение должно совпадать с сервером (NxmUrlReceiver).
    /// </summary>
    public static string PipeName => NxmPipeName.Value;

    /// <summary>Timeout подключения и записи.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Пытается отправить URL в работающий ModsyncManager.
    ///
    /// Возвращает true при успехе, false — при любой ошибке.
    /// Не бросает.
    /// </summary>
    public static async Task<bool> TrySendAsync(
        string url,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            // ConnectAsync с токеном + timeout. .NET 8 не имеет
            // ConnectAsync(int, CancellationToken), поэтому
            // комбинируем через CancellationTokenSource.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            try
            {
                await client.ConnectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout или внешняя отмена. ModsyncManager не отвечает.
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }

            using var writer = new StreamWriter(client)
            {
                AutoFlush = true,
            };

            await writer.WriteLineAsync(url.AsMemory(), cts.Token)
                .ConfigureAwait(false);

            return true;
        }
        catch
        {
            // Любая другая ошибка I/O (pipe занят, доступ запрещён,
            // ModsyncManager упал в момент записи) — считаем, что не доставлено.
            return false;
        }
    }
}
