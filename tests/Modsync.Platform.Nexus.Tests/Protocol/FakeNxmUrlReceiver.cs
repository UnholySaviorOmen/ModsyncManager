using Modsync.Platform.Nexus.Protocol;

namespace Modsync.Platform.Nexus.Tests.Protocol;

/// <summary>
/// Fake INxmUrlReceiver для тестов NexusFreeNxmProvider.
///
/// Режимы:
///   - UrlToReturn != null: WaitForUrlAsync возвращает URL
///     (после опциональной Delay).
///   - UrlToReturn == null и ThrowOnCancel == true: ждёт отмены ct
///     и бросает OperationCanceledException.
///   - UrlToReturn == null и ThrowOnCancel == false:
///     возвращает null сразу (эмуляция «Channel закрыт»).
///
/// TryDrainPendingUrls: считает вызовы, возвращает значение,
/// установленное через PendingUrlsToDrain. Не трогает UrlToReturn.
/// </summary>
public sealed class FakeNxmUrlReceiver : INxmUrlReceiver
{
    public string? UrlToReturn { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public bool ThrowOnCancel { get; set; }

    /// <summary>
    /// Значение, которое вернёт TryDrainPendingUrls.
    /// </summary>
    public int PendingUrlsToDrain { get; set; }

    public int WaitCallCount { get; private set; }

    public int DrainCallCount { get; private set; }

    public bool IsRunning { get; private set; }

    public Task StartAsync(CancellationToken ct)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        return Task.CompletedTask;
    }

    public async Task<string?> WaitForUrlAsync(CancellationToken ct)
    {
        WaitCallCount++;

        if (Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(Delay, ct);
            }
            catch (OperationCanceledException) when (ThrowOnCancel)
            {
                throw;
            }
        }

        if (UrlToReturn is not null)
            return UrlToReturn;

        if (ThrowOnCancel)
        {
            // Ждём отмены ct и бросаем.
            var tcs = new TaskCompletionSource<string?>();
            using var reg = ct.Register(
                () => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }

        // «Channel закрыт» — возвращаем null.
        return null;
    }

    public int TryDrainPendingUrls()
    {
        DrainCallCount++;
        return PendingUrlsToDrain;
    }

    public ValueTask DisposeAsync()
    {
        IsRunning = false;
        return ValueTask.CompletedTask;
    }
}
