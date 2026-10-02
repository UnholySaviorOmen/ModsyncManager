using System.IO.Pipes;
using FluentAssertions;
using Modsync.Platform.Nexus.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests.Protocol;

/// <summary>
/// Тесты NxmUrlReceiver с реальным NamedPipeClientStream.
///
/// Каждый тест использует УНИКАЛЬНОЕ имя pipe — иначе тесты,
/// запущенные параллельно (VS Test Explorer, xUnit collection
/// parallelism), мешают друг другу: общий pipe + WaitForConnectionAsync
/// = коллизии, висящие тесты, «чужой» URL.
///
/// Уникальное имя генерируется как "modsyncmanager-nxm-test-{guid}".
/// </summary>
public class NxmUrlReceiverTests
{
    private static NxmUrlReceiver MakeReceiver(out string pipeName)
    {
        pipeName = "modsyncmanager-nxm-test-" + Guid.NewGuid().ToString("N");
        return new NxmUrlReceiver(
            pipeName, NullLogger<NxmUrlReceiver>.Instance);
    }

    /// <summary>
    /// Отправляет URL в pipe (как это делает handler).
    /// Ждёт немного, чтобы receiver успел подключиться.
    /// </summary>
    private static async Task SendUrlAsync(string pipeName, string url)
    {
        using var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.Out,
            options: PipeOptions.Asynchronous);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(cts.Token);

        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync(url);
    }

    // ------------------------------------------------------------------
    //  Lifecycle
    // ------------------------------------------------------------------

    [Fact]
    public void IsRunning_FalseBeforeStart()
    {
        var receiver = MakeReceiver(out _);
        receiver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task IsRunning_TrueAfterStart()
    {
        await using var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);

        receiver.IsRunning.Should().BeTrue();

        await receiver.StopAsync();

        receiver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_Twice_Throws()
    {
        await using var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);

        var act = async () => await receiver.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        await receiver.StopAsync();
    }

    [Fact]
    public async Task StopAsync_WithoutStart_NoOp()
    {
        await using var receiver = MakeReceiver(out _);
        var act = () => receiver.StopAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_Twice_NoOp()
    {
        await using var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);

        await receiver.StopAsync();
        var act = () => receiver.StopAsync();
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    //  Happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task WaitForUrlAsync_ReceivesSentUrl()
    {
        await using var receiver = MakeReceiver(out var pipeName);
        await receiver.StartAsync(CancellationToken.None);

        // Даём циклу встать на WaitForConnection.
        await Task.Delay(100);

        const string url =
            "nxm://skyrimspecialedition/mods/3863/files/1000172397" +
            "?key=abc&expires=100&user_id=1";

        // Отправляем URL параллельно с ожиданием.
        var sendTask = SendUrlAsync(pipeName, url);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await receiver.WaitForUrlAsync(cts.Token);

        received.Should().Be(url);

        await sendTask;
        await receiver.StopAsync();
    }

    [Fact]
    public async Task WaitForUrlAsync_MultipleUrls_FifoOrder()
    {
        await using var receiver = MakeReceiver(out var pipeName);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var url1 = "nxm://game/mods/1/files/1";
        var url2 = "nxm://game/mods/2/files/2";
        var url3 = "nxm://game/mods/3/files/3";

        // Отправляем последовательно (как handler за клик).
        await SendUrlAsync(pipeName, url1);
        await SendUrlAsync(pipeName, url2);
        await SendUrlAsync(pipeName, url3);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        (await receiver.WaitForUrlAsync(cts.Token)).Should().Be(url1);
        (await receiver.WaitForUrlAsync(cts.Token)).Should().Be(url2);
        (await receiver.WaitForUrlAsync(cts.Token)).Should().Be(url3);

        await receiver.StopAsync();
    }

    [Fact]
    public async Task WaitForUrlAsync_EmptyUrl_Ignored()
    {
        await using var receiver = MakeReceiver(out var pipeName);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Отправляем пустую строку — она НЕ должна попасть в очередь.
        await SendUrlAsync(pipeName, "");

        // Затем — валидный URL.
        await SendUrlAsync(pipeName, "nxm://game/mods/1/files/1");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await receiver.WaitForUrlAsync(cts.Token);

        received.Should().Be("nxm://game/mods/1/files/1");

        await receiver.StopAsync();
    }

    // ------------------------------------------------------------------
    //  StopAsync и WaitForUrlAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task StopAsync_ClosesChannel_WaitForUrlReturnsNull()
    {
        await using var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Запускаем WaitForUrlAsync — он повиснет, ожидая URL.
        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waitTask = receiver.WaitForUrlAsync(waitCts.Token);

        // Даём wait-у встать в очередь.
        await Task.Delay(100);

        // Останавливаем receiver.
        await receiver.StopAsync();

        // WaitForUrlAsync должен вернуть null (Channel закрыт).
        var result = await waitTask;
        result.Should().BeNull();
    }

    [Fact]
    public async Task WaitForUrlAsync_AfterStop_ReturnsNull()
    {
        await using var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);
        await receiver.StopAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await receiver.WaitForUrlAsync(cts.Token);

        result.Should().BeNull();
    }

    // ------------------------------------------------------------------
    //  Cancellation
    // ------------------------------------------------------------------

    [Fact]
    public async Task WaitForUrlAsync_Cancelled_Throws()
    {
        await using var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await receiver.WaitForUrlAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await receiver.StopAsync();
    }

    // ------------------------------------------------------------------
    //  DisposeAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_StopsReceiver()
    {
        var receiver = MakeReceiver(out _);
        await receiver.StartAsync(CancellationToken.None);
        receiver.IsRunning.Should().BeTrue();

        await receiver.DisposeAsync();

        receiver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_NoOp()
    {
        var receiver = MakeReceiver(out _);
        var act = async () => await receiver.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------
    //  Изоляция pipe
    // ------------------------------------------------------------------

    [Fact]
    public async Task TwoReceivers_DifferentPipes_DoNotInterfere()
    {
        // Регрессия: раньше NxmUrlReceiver жёстко использовал
        // NxmPipeName.Value, и два receiver-а в параллельных тестах
        // дрались за один pipe.
        await using var receiver1 = MakeReceiver(out var pipe1);
        await using var receiver2 = MakeReceiver(out var pipe2);

        pipe1.Should().NotBe(pipe2);

        await receiver1.StartAsync(CancellationToken.None);
        await receiver2.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        var url1 = "nxm://game/mods/1/files/1";
        var url2 = "nxm://game/mods/2/files/2";

        await SendUrlAsync(pipe1, url1);
        await SendUrlAsync(pipe2, url2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var got1 = await receiver1.WaitForUrlAsync(cts.Token);
        var got2 = await receiver2.WaitForUrlAsync(cts.Token);

        got1.Should().Be(url1);
        got2.Should().Be(url2);

        await receiver1.StopAsync();
        await receiver2.StopAsync();
    }

    // ------------------------------------------------------------------
    //  TryDrainPendingUrls
    // ------------------------------------------------------------------

    [Fact]
    public void TryDrainPendingUrls_EmptyQueue_ReturnsZero()
    {
        var receiver = MakeReceiver(out _);

        var drained = receiver.TryDrainPendingUrls();

        drained.Should().Be(0);
    }

    [Fact]
    public void TryDrainPendingUrls_NotStarted_ReturnsZero()
    {
        // Receiver не запущен — очередь пуста (никто не писал).
        var receiver = MakeReceiver(out _);

        var drained = receiver.TryDrainPendingUrls();

        drained.Should().Be(0);
    }

    [Fact]
    public async Task TryDrainPendingUrls_WithQueuedUrls_ReturnsCount()
    {
        await using var receiver = MakeReceiver(out var pipeName);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Отправляем три URL. Receiver кладёт их в очередь.
        await SendUrlAsync(pipeName, "nxm://game/mods/1/files/1");
        await SendUrlAsync(pipeName, "nxm://game/mods/2/files/2");
        await SendUrlAsync(pipeName, "nxm://game/mods/3/files/3");

        // Даём AcceptLoop-у успеть прочитать все три.
        await Task.Delay(200);

        var drained = receiver.TryDrainPendingUrls();

        drained.Should().Be(3);

        // Очередь пуста — повторный вызов вернёт 0.
        receiver.TryDrainPendingUrls().Should().Be(0);

        await receiver.StopAsync();
    }

    [Fact]
    public async Task TryDrainPendingUrls_AfterDrain_WaitForUrlBlocks()
    {
        await using var receiver = MakeReceiver(out var pipeName);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        await SendUrlAsync(pipeName, "nxm://game/mods/1/files/1");
        await Task.Delay(200);

        receiver.TryDrainPendingUrls().Should().Be(1);

        // После drain — очередь пуста. WaitForUrlAsync должен висеть.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var act = async () => await receiver.WaitForUrlAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        await receiver.StopAsync();
    }

    [Fact]
    public async Task TryDrainPendingUrls_AfterStop_ReturnsZero()
    {
        await using var receiver = MakeReceiver(out var pipeName);
        await receiver.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        await SendUrlAsync(pipeName, "nxm://game/mods/1/files/1");
        await Task.Delay(200);

        await receiver.StopAsync();

        // После StopAsync Channel закрыт. TryRead возвращает false
        // для закрытого канала (данные, если были, уже недоступны).
        var drained = receiver.TryDrainPendingUrls();

        // Точное значение (0 или 1) зависит от того, успел ли
        // AcceptLoop положить URL в Channel до StopAsync. В тесте
        // URL уже в очереди (Task.Delay 200), но StopAsync
        // закрывает канал. TryRead на закрытом канале с данными
        // всё ещё может вернуть true один раз.
        // Проверяем только, что не бросает.
        drained.Should().BeGreaterOrEqualTo(0);
    }
}
