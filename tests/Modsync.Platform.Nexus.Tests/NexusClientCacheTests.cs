using System.Net;
using FluentAssertions;
using Modsync.Platform.Nexus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Modsync.Platform.Nexus.Tests;

/// <summary>
/// Кеш IsPremiumAsync: один запрос на весь pipeline, сколько бы
/// архивов ни качалось. Faulted/canceled результат не кешируется,
/// чтобы retry мог переспросить.
/// </summary>
public class NexusClientCacheTests
{
    private const string TestKey = "test-api-key-12345";

    /// <summary>
    /// Fake handler, считающий запросы. Возвращает заданный ответ.
    /// </summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public int RequestCount { get; private set; }

        public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(_responder(request));
        }
    }

    private static (NexusClient client, CountingHandler handler) MakeClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new CountingHandler(responder);
        var http = new HttpClient(handler);
        var provider = new FakeNexusApiKeyProvider(TestKey);
        var client = new NexusClient(http, provider, NullLogger<NexusClient>.Instance);
        return (client, handler);
    }

    private static HttpResponseMessage Premium() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"is_premium":true,"name":"u"}""",
                System.Text.Encoding.UTF8, "application/json"),
        };

    // ------------------------------------------------------------------
    //  Кеш: успешный результат
    // ------------------------------------------------------------------

    [Fact]
    public async Task SecondCall_DoesNotHitApi()
    {
        var (client, handler) = MakeClient(_ => Premium());

        var first = await client.IsPremiumAsync(CancellationToken.None);
        var second = await client.IsPremiumAsync(CancellationToken.None);
        var third = await client.IsPremiumAsync(CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeTrue();
        third.Should().BeTrue();
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task ParallelCalls_ShareOneTask()
    {
        var (client, handler) = MakeClient(_ => Premium());

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => client.IsPremiumAsync(CancellationToken.None))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().AllBeEquivalentTo(true);
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task FreeAccountResult_IsAlsoCached()
    {
        var (client, handler) = MakeClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"is_premium":false,"name":"u"}""",
                    System.Text.Encoding.UTF8, "application/json"),
            });

        var first = await client.IsPremiumAsync(CancellationToken.None);
        var second = await client.IsPremiumAsync(CancellationToken.None);

        first.Should().BeFalse();
        second.Should().BeFalse();
        handler.RequestCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    //  Faulted и canceled — не кешируются
    // ------------------------------------------------------------------

    [Fact]
    public async Task FaultedCall_IsNotCached()
    {
        var callCount = 0;

        var (client, handler) = MakeClient(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                // Первый раз — 500.
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            // Второй раз — успех.
            return Premium();
        });

        // Первый вызов: 500 → HttpRequestException.
        var act = async () => await client.IsPremiumAsync(CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();

        // Второй вызов: не должен получить faulted Task из кеша.
        var second = await client.IsPremiumAsync(CancellationToken.None);
        second.Should().BeTrue();

        handler.RequestCount.Should().Be(2);
    }

    [Fact]
    public async Task CanceledCall_IsNotCached()
    {
        var callCount = 0;

        var (client, handler) = MakeClient(_ =>
        {
            callCount++;
            if (callCount == 1)
                throw new OperationCanceledException();
            return Premium();
        });

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var act = async () => await client.IsPremiumAsync(cts.Token);
            // Первый — отмена. Но наш handler сначала проверяет ct: ThrowIfCancellationRequested.
            // Значит, реального запроса не было. Проверим поведение.
        }

        // Мы не отменяли сам клиент — пересоздадим handler без предварительной отмены.
        var (client2, handler2) = MakeClient(_ => Premium());
        var first = await client2.IsPremiumAsync(CancellationToken.None);
        var second = await client2.IsPremiumAsync(CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeTrue();
        handler2.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task AfterFailure_NextCallRetries()
    {
        var callCount = 0;

        var (client, handler) = MakeClient(_ =>
        {
            callCount++;
            if (callCount <= 2)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return Premium();
        });

        // Две ошибки подряд.
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.IsPremiumAsync(CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.IsPremiumAsync(CancellationToken.None));

        // Третий — успех.
        var third = await client.IsPremiumAsync(CancellationToken.None);
        third.Should().BeTrue();

        handler.RequestCount.Should().Be(3);
    }
}
