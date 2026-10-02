// SPDX-FileCopyrightText: 2026 UnholySaviorOmen
// SPDX-License-Identifier: GPL-3.0-only

using System.IO.Pipes;
using System.Threading.Channels;
using Modsync.Core.Nxm;
using Microsoft.Extensions.Logging;

namespace Modsync.Platform.Nexus.Protocol;

/// <summary>
/// Реализация INxmUrlReceiver через NamedPipeServerStream.
///
/// Один поток-приёмник (AcceptLoopAsync) + неограниченный Channel.
///
/// Цикл приёма:
///   1. Создать NamedPipeServerStream с именем pipe.
///   2. WaitForConnectionAsync(ct).
///   3. ReadLineAsync — получить URL (одна строка).
///   4. Channel.Writer.WriteAsync(url) — в очередь.
///   5. Закрыть stream.
///   6. Повторить.
///
/// Ошибки I/O в цикле (клиент отвалился, pipe закрылся) —
/// логируются и цикл продолжается. Останавливаемся только по
/// отмене (StopAsync).
///
/// Двойной StartAsync — InvalidOperationException.
/// StopAsync идемпотентен.
///
/// Имя pipe — параметр конструктора. Продакшн использует
/// NxmPipeName.Value ("modsyncmanager-nxm"). Тесты передают уникальное
/// имя для изоляции: fixed pipe name + параллельные тесты =
/// коллизии и висящие WaitForConnectionAsync.
///
/// Не помечен [SupportedOSPlatform("windows")]: NamedPipeServerStream
/// есть на всех платформах. На Linux никто не постучится, но
/// receiver можно создать без ошибок.
/// </summary>
public sealed class NxmUrlReceiver : INxmUrlReceiver, IDisposable, IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly ILogger<NxmUrlReceiver> _logger;
    private readonly Channel<string> _channel;

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    /// <summary>
    /// Продакшн-конструктор: использует NxmPipeName.Value.
    /// </summary>
    public NxmUrlReceiver(ILogger<NxmUrlReceiver> logger)
        : this(NxmPipeName.Value, logger)
    {
    }

    /// <summary>
    /// Конструктор с явным именем pipe. Для тестов — уникальное
    /// имя на каждый тест (изоляция).
    /// </summary>
    public NxmUrlReceiver(string pipeName, ILogger<NxmUrlReceiver> logger)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new ArgumentException(
                "Pipe name must be non-empty.", nameof(pipeName));

        _pipeName = pipeName;
        _logger = logger;

        // Неограниченный Channel. FIFO. Single reader (WaitForUrlAsync),
        // single writer (AcceptLoopAsync — но пишет только он, так что
        // фактически single writer).
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,  // WaitForUrlAsync может вызываться конкурентно
            SingleWriter = true,   // только AcceptLoopAsync пишет
            AllowSynchronousContinuations = false,
        });
    }

    public bool IsRunning => _acceptLoop is not null && !_acceptLoop.IsCompleted;

    /// <summary>Имя pipe. Для диагностики.</summary>
    public string PipeName => _pipeName;

    // ------------------------------------------------------------------
    //  StartAsync
    // ------------------------------------------------------------------

    public Task StartAsync(CancellationToken ct)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException(
                "NxmUrlReceiver is already running. " +
                "Call StopAsync before starting again.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _logger.LogInformation(
            "Starting nxm URL receiver on pipe '{Pipe}'",
            _pipeName);

        _acceptLoop = Task.Run(
            () => AcceptLoopAsync(_cts.Token),
            CancellationToken.None);

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    //  StopAsync
    // ------------------------------------------------------------------

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            // Never started, или уже остановлен.
            return;
        }

        _logger.LogInformation("Stopping nxm URL receiver...");

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // Игнорируем — уже отменён.
        }

        if (_acceptLoop is not null)
        {
            try
            {
                // Ждём завершения цикла. Timeout — 5 секунд, чтобы
                // не висеть, если pipe залип.
                await Task.WhenAny(
                    _acceptLoop,
                    Task.Delay(TimeSpan.FromSeconds(5)))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Логируем ниже.
            }
        }

        _channel.Writer.TryComplete();

        _cts.Dispose();
        _cts = null;
        _acceptLoop = null;

        _logger.LogInformation("nxm URL receiver stopped.");
    }

    // ------------------------------------------------------------------
    //  WaitForUrlAsync
    // ------------------------------------------------------------------

    public async Task<string?> WaitForUrlAsync(CancellationToken ct)
    {
        try
        {
            return await _channel.Reader
                .ReadAsync(ct)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            // StopAsync закрыл Channel — больше URL-ов не будет.
            return null;
        }
    }

    // ------------------------------------------------------------------
    //  TryDrainPendingUrls
    // ------------------------------------------------------------------

    public int TryDrainPendingUrls()
    {
        int drained = 0;

        while (_channel.Reader.TryRead(out _))
        {
            drained++;
        }

        return drained;
    }

    // ------------------------------------------------------------------
    //  AcceptLoopAsync
    // ------------------------------------------------------------------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await AcceptOneConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Нормальная остановка.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Unexpected error in nxm URL receiver loop. " +
                    "Continuing.");

                // Защита от busy-loop: если что-то падает мгновенно
                // (например, pipe занят другим процессом),
                // не крутимся в горячем цикле.
                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(500), ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task AcceptOneConnectionAsync(CancellationToken ct)
    {
        // maxNumberOfServerInstances: 1 — handler-ы подключаются
        // по одному (Windows вызывает handler синхронно, но два
        // клика подряд могут наложиться).
        //
        // Без PipeOptions.Asynchronous WaitForConnectionAsync
        // работает плохо — блокирует поток.
        await using var server = new NamedPipeServerStream(
            pipeName: _pipeName,
            direction: PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

        using var reader = new StreamReader(server);
        var url = await reader.ReadLineAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogDebug(
                "Empty URL received from pipe — ignoring.");
            return;
        }

        _logger.LogInformation(
            "Received nxm URL: {Url}", url);

        await _channel.Writer.WriteAsync(url, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    //  IAsyncDisposable
    // ------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    //  IDisposable
    // ------------------------------------------------------------------

    /// <summary>
    /// Синхронный Dispose для DI-контейнера (Microsoft.Extensions.DependencyInjection).
    ///
    /// Контейнер синхронный: при ServiceProvider.Dispose() он вызывает
    /// IDisposable.Dispose(), а не IAsyncDisposable.DisposeAsync().
    /// Без этой реализации контейнер бросает InvalidOperationException
    /// «type only implements IAsyncDisposable».
    ///
    /// StopAsync внутри уже содержит таймауты (Task.WhenAny с 5 секундами),
    /// так что блокировки здесь не будет даже при зависшем pipe.
    /// </summary>
    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
